using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public record MatchFilter(int SeasonId, int? CompetitionId = null, int? PhaseId = null, DateOnly? Day = null,
    bool OnlyMissingResult = false, int? ClubId = null);

public record AdminMatchItem(int Id, string Competition, string CompetitionColor, string Phase, string? GroupOrRound,
    int? Matchday, DateTimeOffset? KickoffAt, MatchStatus Status,
    int? HomeId, string Home, string? HomeColor, string? HomeColor2, string? HomeLogo,
    int? AwayId, string Away, string? AwayColor, string? AwayColor2, string? AwayLogo,
    int? HomeScore, int? AwayScore, int? HomePenalties, int? AwayPenalties, string? Stadium);

public record ProposedFixture(int Matchday, DateTimeOffset KickoffAt, int HomeId, string Home, int AwayId, string Away);

public class ScheduleService(IAppDbContext db)
{
    public async Task<List<AdminMatchItem>> ListAsync(MatchFilter f, CancellationToken ct = default)
    {
        var q = db.Matches.AsNoTracking().Where(m => m.Phase.Competition.SeasonId == f.SeasonId);
        if (f.CompetitionId is { } c) q = q.Where(m => m.Phase.CompetitionId == c);
        if (f.PhaseId is { } p) q = q.Where(m => m.PhaseId == p);
        if (f.ClubId is { } club) q = q.Where(m => m.HomeClubId == club || m.AwayClubId == club);
        if (f.Day is { } day)
        {
            var from = KokoraTime.StartOfDayUtc(day);
            var to = KokoraTime.StartOfDayUtc(day.AddDays(1));
            q = q.Where(m => m.KickoffAt >= from && m.KickoffAt < to);
        }
        if (f.OnlyMissingResult)
        {
            var now = DateTimeOffset.UtcNow;
            q = q.Where(m => m.KickoffAt < now && (m.Status == MatchStatus.Scheduled || m.Status == MatchStatus.Live || m.Status == MatchStatus.HalfTime));
        }

        return await q
            .OrderBy(m => m.KickoffAt == null).ThenBy(m => m.KickoffAt).ThenBy(m => m.Phase.Competition.Order).ThenBy(m => m.Id)
            .Select(m => new AdminMatchItem(m.Id, m.Phase.Competition.Name, m.Phase.Competition.Color, m.Phase.Name,
                m.Group != null ? m.Group.Name : m.Round != null ? m.Round.Name : null, m.Matchday, m.KickoffAt, m.Status,
                m.HomeClubId, m.HomeClub != null ? m.HomeClub.Name : m.HomePlaceholder ?? "À désigner",
                m.HomeClub != null ? m.HomeClub.PrimaryColor : null, m.HomeClub != null ? m.HomeClub.SecondaryColor : null,
                m.HomeClub != null ? m.HomeClub.LogoPath : null,
                m.AwayClubId, m.AwayClub != null ? m.AwayClub.Name : m.AwayPlaceholder ?? "À désigner",
                m.AwayClub != null ? m.AwayClub.PrimaryColor : null, m.AwayClub != null ? m.AwayClub.SecondaryColor : null,
                m.AwayClub != null ? m.AwayClub.LogoPath : null,
                m.HomeScore, m.AwayScore, m.HomePenalties, m.AwayPenalties, m.Stadium != null ? m.Stadium.Name : null))
            .ToListAsync(ct);
    }

    public async Task<Match> GetAsync(int id, CancellationToken ct = default) =>
        await db.Matches.Include(m => m.Phase).ThenInclude(p => p.Competition)
            .Include(m => m.HomeClub).Include(m => m.AwayClub).Include(m => m.Group).Include(m => m.Round)
            .FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("Match");

    public static MatchInput ToInput(Match m) => new()
    {
        Id = m.Id, PhaseId = m.PhaseId, GroupId = m.GroupId, RoundId = m.RoundId, Matchday = m.Matchday,
        HomeClubId = m.HomeClubId, AwayClubId = m.AwayClubId, HomePlaceholder = m.HomePlaceholder, AwayPlaceholder = m.AwayPlaceholder,
        KickoffLocal = m.KickoffAt is { } k ? KokoraTime.ToLocal(k).DateTime : null,
        StadiumId = m.StadiumId, RefereeId = m.RefereeId, IsFeatured = m.IsFeatured, Notes = m.Notes
    };

    public async Task<int> SaveAsync(MatchInput input, CancellationToken ct = default)
    {
        var phase = await db.Phases.FirstOrDefaultAsync(p => p.Id == input.PhaseId, ct) ?? throw new NotFoundException("Phase");

        if (input.HomeClubId is not null && input.HomeClubId == input.AwayClubId)
            throw new BusinessRuleException("Une équipe ne peut pas jouer contre elle-même.", nameof(input.AwayClubId));
        if (input.HomeClubId is null && string.IsNullOrWhiteSpace(input.HomePlaceholder))
            throw new BusinessRuleException("Choisissez l'équipe à domicile ou indiquez un libellé (ex. « Vainqueur QF1 »).", nameof(input.HomeClubId));
        if (input.AwayClubId is null && string.IsNullOrWhiteSpace(input.AwayPlaceholder))
            throw new BusinessRuleException("Choisissez l'équipe à l'extérieur ou indiquez un libellé.", nameof(input.AwayClubId));

        if (phase.Type == PhaseType.League)
        {
            if (input.GroupId is null)
                throw new BusinessRuleException("Choisissez la poule.", nameof(input.GroupId));
            var group = await db.Groups.Include(g => g.Teams).FirstOrDefaultAsync(g => g.Id == input.GroupId && g.PhaseId == phase.Id, ct)
                ?? throw new BusinessRuleException("Cette poule n'appartient pas à la phase choisie.", nameof(input.GroupId));
            foreach (var clubId in new[] { input.HomeClubId, input.AwayClubId })
                if (clubId is not null && group.Teams.Count > 0 && group.Teams.All(t => t.ClubId != clubId))
                    throw new BusinessRuleException($"Une des équipes ne fait pas partie de la poule {group.Name}.", nameof(input.HomeClubId));
            input.RoundId = null;
        }
        else
        {
            if (input.RoundId is not null && !await db.Rounds.AnyAsync(r => r.Id == input.RoundId && r.PhaseId == phase.Id, ct))
                throw new BusinessRuleException("Ce tour n'appartient pas à la phase choisie.", nameof(input.RoundId));
            input.GroupId = null;
        }

        DateTimeOffset? kickoff = input.KickoffLocal is { } local ? ToUtc(local) : null;

        var match = input.Id == 0 ? new Match() : await db.Matches.FindAsync([input.Id], ct) ?? throw new NotFoundException("Match");

        if (kickoff is not null)
        {
            var dayStart = KokoraTime.StartOfDayUtc(DateOnly.FromDateTime(input.KickoffLocal!.Value));
            var dayEnd = dayStart.AddDays(1);
            var clubs = new[] { input.HomeClubId, input.AwayClubId }.Where(x => x is not null).ToList();
            var clash = await db.Matches
                .Where(m => m.Id != match.Id && m.KickoffAt >= dayStart && m.KickoffAt < dayEnd
                    && m.Status != MatchStatus.Postponed
                    && (clubs.Contains(m.HomeClubId) || clubs.Contains(m.AwayClubId)))
                .Select(m => (m.HomeClub != null ? m.HomeClub.Name : "?") + " - " + (m.AwayClub != null ? m.AwayClub.Name : "?"))
                .FirstOrDefaultAsync(ct);
            if (clash is not null)
                throw new BusinessRuleException($"Une des équipes joue déjà ce jour-là ({clash}).", nameof(input.KickoffLocal));
        }

        if (input.Id != 0 && match.PhaseId != input.PhaseId && match.Status is MatchStatus.Finished or MatchStatus.Forfeit)
            throw new BusinessRuleException("Un match joué ne peut pas changer de phase.");

        match.PhaseId = input.PhaseId;
        match.GroupId = input.GroupId;
        match.RoundId = input.RoundId;
        match.Matchday = input.Matchday;
        match.HomeClubId = input.HomeClubId;
        match.AwayClubId = input.AwayClubId;
        match.HomePlaceholder = input.HomeClubId is null ? input.HomePlaceholder?.Trim() : null;
        match.AwayPlaceholder = input.AwayClubId is null ? input.AwayPlaceholder?.Trim() : null;
        match.KickoffAt = kickoff;
        match.StadiumId = input.StadiumId;
        match.RefereeId = input.RefereeId;
        match.IsFeatured = input.IsFeatured;
        match.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        if (input.Id == 0) db.Matches.Add(match);
        await db.SaveChangesAsync(ct);
        return match.Id;
    }

    /// <summary>Reporte un match : sans nouvelle date il passe « Reporté », avec une date il est reprogrammé.</summary>
    public async Task PostponeAsync(int id, DateTime? newKickoffLocal, CancellationToken ct = default)
    {
        var m = await db.Matches.FindAsync([id], ct) ?? throw new NotFoundException("Match");
        if (m.Status is MatchStatus.Finished or MatchStatus.Forfeit or MatchStatus.Live or MatchStatus.HalfTime)
            throw new BusinessRuleException("Ce match a commencé ou est terminé : il ne peut pas être reporté.");

        var previous = m.KickoffAt is { } k ? KokoraTime.Long(k) : null;
        if (newKickoffLocal is { } local)
        {
            m.KickoffAt = ToUtc(local);
            m.Status = MatchStatus.Scheduled;
            if (previous is not null) m.Notes = AppendNote(m.Notes, $"Reporté (initialement prévu le {previous}).");
        }
        else
        {
            m.Status = MatchStatus.Postponed;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var m = await db.Matches.FindAsync([id], ct) ?? throw new NotFoundException("Match");
        if (m.Status is not (MatchStatus.Scheduled or MatchStatus.Postponed))
            throw new BusinessRuleException("Seul un match non joué peut être supprimé.");
        if (m.RoundId is not null)
            throw new BusinessRuleException("Ce match fait partie d'un tableau à élimination : supprimez le tableau depuis la phase.");
        db.Matches.Remove(m);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ Génération d'une poule

    public async Task<List<ProposedFixture>> PreviewGroupFixturesAsync(FixtureGenerationInput input, CancellationToken ct = default)
    {
        var group = await db.Groups.AsNoTracking().Include(g => g.Teams).ThenInclude(t => t.Club)
            .FirstOrDefaultAsync(g => g.Id == input.GroupId, ct) ?? throw new NotFoundException("Poule");
        if (group.Teams.Count < 2)
            throw new BusinessRuleException("Ajoutez au moins deux équipes à la poule.");
        if (input.FirstDate is null)
            throw new BusinessRuleException("Indiquez la date de la 1re journée.", nameof(input.FirstDate));

        var names = group.Teams.ToDictionary(t => t.ClubId, t => t.Club.Name);
        var fixtures = RoundRobin.Generate(group.Teams.OrderBy(t => t.Seed).Select(t => t.ClubId).ToList(), input.HomeAndAway);

        return fixtures
            .GroupBy(f => f.Matchday)
            .SelectMany(day => day.Select((f, i) =>
            {
                var date = input.FirstDate.Value.AddDays((f.Matchday - 1) * input.DaysBetweenMatchdays);
                var local = date.ToDateTime(input.FirstKickoff).AddMinutes(i * input.MinutesBetweenMatches);
                return new ProposedFixture(f.Matchday, ToUtc(local), f.HomeId, names[f.HomeId], f.AwayId, names[f.AwayId]);
            }))
            .ToList();
    }

    public async Task<int> GenerateGroupFixturesAsync(FixtureGenerationInput input, CancellationToken ct = default)
    {
        var proposed = await PreviewGroupFixturesAsync(input, ct);
        var group = await db.Groups.FirstAsync(g => g.Id == input.GroupId, ct);

        var existing = await db.Matches.Where(m => m.GroupId == group.Id).ToListAsync(ct);
        if (input.ReplaceUnplayed)
        {
            db.Matches.RemoveRange(existing.Where(m => m.Status is MatchStatus.Scheduled or MatchStatus.Postponed));
            existing = existing.Where(m => m.Status is not (MatchStatus.Scheduled or MatchStatus.Postponed)).ToList();
        }
        else if (existing.Count > 0)
            throw new BusinessRuleException("Cette poule a déjà des matchs. Cochez « Supprimer d'abord les matchs non joués » pour les remplacer.",
                nameof(input.ReplaceUnplayed));

        // Ne recrée pas une affiche déjà jouée (même ordre domicile/extérieur).
        var played = existing.Select(m => (m.HomeClubId, m.AwayClubId)).ToHashSet();
        var created = 0;
        foreach (var f in proposed.Where(f => !played.Contains((f.HomeId, f.AwayId))))
        {
            db.Matches.Add(new Match
            {
                PhaseId = group.PhaseId, GroupId = group.Id, Matchday = f.Matchday, HomeClubId = f.HomeId, AwayClubId = f.AwayId,
                KickoffAt = f.KickoffAt, StadiumId = input.StadiumId
            });
            created++;
        }
        await db.SaveChangesAsync(ct);
        return created;
    }

    public static DateTimeOffset ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, KokoraTime.Zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private static string AppendNote(string? notes, string line) =>
        string.IsNullOrWhiteSpace(notes) ? line : $"{notes.TrimEnd()}\n{line}";
}
