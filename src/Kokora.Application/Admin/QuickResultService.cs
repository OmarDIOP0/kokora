using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public class QuickResultRow
{
    public int MatchId { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public int? HomePenalties { get; set; }
    public int? AwayPenalties { get; set; }
    /// <summary>« home » ou « away » : équipe déclarée forfait.</summary>
    public string? Forfeit { get; set; }
    public int? ManOfTheMatchPlayerId { get; set; }

    public bool IsFilled => (HomeScore is not null && AwayScore is not null) || Forfeit is "home" or "away";
}

public record QuickPlayer(int Id, string Name, int? Number);

public record QuickMatchVm(int Id, DateTimeOffset? KickoffAt, string Competition, string CompetitionColor, string Stage,
    TeamVm Home, TeamVm Away, bool Knockout, IReadOnlyList<QuickPlayer> HomeSquad, IReadOnlyList<QuickPlayer> AwaySquad,
    int? HomeScore = null, int? AwayScore = null);

public record QuickSaveReport(int Saved, Dictionary<int, string> Errors);

/// <summary>
/// Saisie rapide des résultats : tous les matchs passés sans résultat sur une page, un score par ligne
/// (ou un forfait) et l'homme du match. Buteurs et cartons se complètent ensuite dans la fiche du match.
/// </summary>
public class QuickResultService(IAppDbContext db, ResultService results, CompetitionCache cache)
{
    /// <summary>Matchs passés sans résultat (coup d'envoi dépassé, pas encore saisis).</summary>
    public Task<List<QuickMatchVm>> PendingAsync(int seasonId, int? competitionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return ListAsync(seasonId, db.Matches.Where(m => (competitionId == null || m.Phase.CompetitionId == competitionId) && m.KickoffAt < now
            && (m.Status == MatchStatus.Scheduled || m.Status == MatchStatus.Live || m.Status == MatchStatus.HalfTime)), ct);
    }

    /// <summary>Matchs joués dont l'homme du match n'a pas encore été désigné (les plus récents d'abord).</summary>
    public Task<List<QuickMatchVm>> WithoutManOfTheMatchAsync(int seasonId, int? competitionId, CancellationToken ct = default) =>
        ListAsync(seasonId, db.Matches.Where(m => (competitionId == null || m.Phase.CompetitionId == competitionId)
            && m.ManOfTheMatchPlayerId == null && (m.Status == MatchStatus.Finished || m.Status == MatchStatus.UnderReview)), ct, newestFirst: true);

    private async Task<List<QuickMatchVm>> ListAsync(int seasonId, IQueryable<Kokora.Domain.Matches.Match> query, CancellationToken ct, bool newestFirst = false)
    {
        query = query.Where(m => m.Phase.Competition.SeasonId == seasonId && m.HomeClubId != null && m.AwayClubId != null);
        query = newestFirst ? query.OrderByDescending(m => m.KickoffAt) : query.OrderBy(m => m.KickoffAt);
        var matches = await query.WithRowData().Take(80).ToListAsync(ct);
        var clubIds = matches.SelectMany(m => new[] { m.HomeClubId!.Value, m.AwayClubId!.Value }).Distinct().ToList();
        var squads = (await db.SquadMembers.AsNoTracking().Where(s => s.SeasonId == seasonId && clubIds.Contains(s.ClubId))
                .Select(s => new { s.ClubId, s.PlayerId, s.ShirtNumber, Name = s.Player.Nickname ?? s.Player.FirstName + " " + s.Player.LastName })
                .ToListAsync(ct))
            .GroupBy(s => s.ClubId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.ShirtNumber ?? 99).ThenBy(s => s.Name)
                .Select(s => new QuickPlayer(s.PlayerId, s.Name, s.ShirtNumber)).ToList());
        return matches.Select(m => new QuickMatchVm(m.Id, m.KickoffAt, m.Phase.Competition.Name, m.Phase.Competition.Color, Mapping.Stage(m),
                Mapping.Team(m.HomeClub)!, Mapping.Team(m.AwayClub)!, m.Phase.Type == PhaseType.Knockout,
                squads.GetValueOrDefault(m.HomeClubId!.Value, []), squads.GetValueOrDefault(m.AwayClubId!.Value, []), m.HomeScore, m.AwayScore))
            .ToList();
    }

    /// <summary>Chaque ligne est enregistrée séparément : une erreur n'empêche pas les autres.</summary>
    public async Task<QuickSaveReport> SaveAsync(IEnumerable<QuickResultRow> rows, CancellationToken ct = default)
    {
        var saved = 0;
        var errors = new Dictionary<int, string>();
        foreach (var row in rows.Where(r => r.IsFilled))
        {
            var forfeit = row.Forfeit is "home" or "away";
            try
            {
                // Base : le résultat existant (buts déjà saisis en direct, notes…), complété par la ligne.
                var m = await results.GetAsync(row.MatchId, ct);
                var input = ResultService.ToInput(m);
                input.Status = forfeit ? MatchStatus.Forfeit : MatchStatus.Finished;
                input.HomeScore = row.HomeScore;
                input.AwayScore = row.AwayScore;
                input.HomePenalties = row.HomePenalties;
                input.AwayPenalties = row.AwayPenalties;
                input.ForfeitingClubId = row.Forfeit == "home" ? m.HomeClubId : row.Forfeit == "away" ? m.AwayClubId : null;
                input.ManOfTheMatchPlayerId = forfeit ? null : row.ManOfTheMatchPlayerId;
                await results.SaveAsync(input, ct);
                saved++;
            }
            catch (NotFoundException) { errors[row.MatchId] = "Match introuvable."; }
            catch (BusinessRuleException ex) { errors[row.MatchId] = ex.Message; }
        }
        return new QuickSaveReport(saved, errors);
    }

    /// <summary>Hommes du match des matchs déjà enregistrés (match → joueur).</summary>
    public async Task<QuickSaveReport> SaveManOfTheMatchAsync(IEnumerable<QuickResultRow> rows, CancellationToken ct = default)
    {
        var saved = 0;
        var errors = new Dictionary<int, string>();
        var touched = new HashSet<int>();
        foreach (var row in rows.Where(r => r.ManOfTheMatchPlayerId is not null))
        {
            var m = await db.Matches.Include(x => x.Phase).ThenInclude(p => p.Competition).FirstOrDefaultAsync(x => x.Id == row.MatchId, ct);
            if (m is null) { errors[row.MatchId] = "Match introuvable."; continue; }
            var ok = await db.SquadMembers.AnyAsync(s => s.SeasonId == m.Phase.Competition.SeasonId && s.PlayerId == row.ManOfTheMatchPlayerId
                && (s.ClubId == m.HomeClubId || s.ClubId == m.AwayClubId), ct);
            if (!ok) { errors[row.MatchId] = "Ce joueur n'est dans aucune des deux équipes."; continue; }
            m.ManOfTheMatchPlayerId = row.ManOfTheMatchPlayerId;
            touched.Add(m.Phase.CompetitionId);
            saved++;
        }
        await db.SaveChangesAsync(ct);
        foreach (var id in touched) cache.Invalidate(id); // statistiques recalculées
        return new QuickSaveReport(saved, errors);
    }
}
