using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Engagement;

public record VoteCandidate(int PlayerId, string Name, int? Number, bool IsHome);

public record VoteResult(int PlayerId, string Name, string? Slug, bool IsHome, int Votes, int Pct);

public record VoteBlock(bool Open, DateTimeOffset? ClosesAt, int? MyVote, int Total,
    IReadOnlyList<VoteResult> Results, IReadOnlyList<VoteCandidate> Candidates, string HomeTeam = "", string AwayTeam = "");

/// <summary>
/// Homme du match voté par les supporters connectés : ouvert à la fin du match pendant 48 heures,
/// un vote par compte (modifiable tant que le vote est ouvert).
/// </summary>
public class VoteService(IAppDbContext db)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(48);

    /// <summary>Fin estimée du match (coup d'envoi + 2 h) : le vote se ferme 48 h après.</summary>
    public static DateTimeOffset? ClosesAt(Match m) => m.KickoffAt?.AddHours(2).Add(Window);

    public static bool IsOpen(Match m, DateTimeOffset now) =>
        m.Status is MatchStatus.Finished or MatchStatus.UnderReview && m.HomeClubId is not null && m.AwayClubId is not null
        && ClosesAt(m) is { } c && now < c;

    public async Task<VoteBlock?> BlockAsync(int matchId, string? userId, CancellationToken ct = default)
    {
        var m = await db.Matches.AsNoTracking().Include(x => x.Phase).ThenInclude(p => p.Competition)
            .Include(x => x.HomeClub).Include(x => x.AwayClub)
            .FirstOrDefaultAsync(x => x.Id == matchId, ct);
        if (m is null || m.HomeClubId is null || m.Status is not (MatchStatus.Finished or MatchStatus.UnderReview)) return null;
        var now = DateTimeOffset.UtcNow;
        var open = IsOpen(m, now);
        var votes = await db.ManOfTheMatchVotes.AsNoTracking().Where(v => v.MatchId == matchId)
            .Select(v => new { v.UserId, v.PlayerId }).ToListAsync(ct);
        if (!open && votes.Count == 0) return null;

        var candidates = open ? await CandidatesAsync(m, ct) : [];
        var ids = votes.Select(v => v.PlayerId).Distinct().ToList();
        var players = await db.Players.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.Nickname ?? p.FirstName + " " + p.LastName, p.Slug }).ToDictionaryAsync(p => p.Id, ct);
        var homeIds = await db.SquadMembers.AsNoTracking()
            .Where(s => s.SeasonId == m.Phase.Competition.SeasonId && s.ClubId == m.HomeClubId && ids.Contains(s.PlayerId))
            .Select(s => s.PlayerId).ToListAsync(ct);
        var results = votes.GroupBy(v => v.PlayerId).Where(g => players.ContainsKey(g.Key))
            .Select(g => new VoteResult(g.Key, players[g.Key].Name, players[g.Key].Slug, homeIds.Contains(g.Key), g.Count(),
                (int)Math.Round(100.0 * g.Count() / votes.Count)))
            .OrderByDescending(r => r.Votes).ThenBy(r => r.Name).Take(5).ToList();
        return new VoteBlock(open, ClosesAt(m), userId is null ? null : votes.FirstOrDefault(v => v.UserId == userId)?.PlayerId,
            votes.Count, results, candidates, m.HomeClub?.ShortName ?? "", m.AwayClub?.ShortName ?? "");
    }

    /// <summary>Joueurs alignés (composition saisie), sinon les effectifs des deux équipes.</summary>
    private async Task<List<VoteCandidate>> CandidatesAsync(Match m, CancellationToken ct)
    {
        var seasonId = m.Phase.Competition.SeasonId;
        var squads = await db.SquadMembers.AsNoTracking().Include(s => s.Player)
            .Where(s => s.SeasonId == seasonId && (s.ClubId == m.HomeClubId || s.ClubId == m.AwayClubId)).ToListAsync(ct);
        var lineup = await db.LineupEntries.AsNoTracking().Where(l => l.MatchId == m.Id).Select(l => l.PlayerId).ToListAsync(ct);
        // Sans composition saisie, tout l'effectif est proposé (un gardien décisif ne figure pas dans les buteurs).
        var pool = lineup.Count > 0 ? squads.Where(s => lineup.Contains(s.PlayerId)) : squads;
        return pool.OrderByDescending(s => s.ClubId == m.HomeClubId).ThenBy(s => s.ShirtNumber ?? 99)
            .Select(s => new VoteCandidate(s.PlayerId, s.Player.Nickname ?? $"{s.Player.FirstName} {s.Player.LastName}", s.ShirtNumber,
                s.ClubId == m.HomeClubId)).ToList();
    }

    public async Task VoteAsync(int matchId, string userId, int playerId, CancellationToken ct = default)
    {
        var m = await db.Matches.AsNoTracking().Include(x => x.Phase).ThenInclude(p => p.Competition)
            .FirstOrDefaultAsync(x => x.Id == matchId, ct) ?? throw new NotFoundException("Match");
        if (!IsOpen(m, DateTimeOffset.UtcNow)) throw new BusinessRuleException("Le vote est fermé pour ce match.");
        if (!(await CandidatesAsync(m, ct)).Any(c => c.PlayerId == playerId))
            throw new BusinessRuleException("Ce joueur ne fait pas partie des joueurs du match.");
        var vote = await db.ManOfTheMatchVotes.FirstOrDefaultAsync(v => v.MatchId == matchId && v.UserId == userId, ct);
        if (vote is null) db.ManOfTheMatchVotes.Add(vote = new ManOfTheMatchVote { MatchId = matchId, UserId = userId });
        vote.PlayerId = playerId;
        await db.SaveChangesAsync(ct);
    }
}
