using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Public;

/// <summary>Lectures publiques : matchs du jour, à venir, résultats, direct, fiche match.</summary>
public class MatchQueryService(IAppDbContext db)
{
    private static readonly MatchStatus[] Played = [MatchStatus.Finished, MatchStatus.Forfeit, MatchStatus.UnderReview, MatchStatus.Abandoned];
    private static readonly MatchStatus[] LiveStatuses = [MatchStatus.Live, MatchStatus.HalfTime];

    public async Task<SeasonVm?> CurrentSeasonAsync(CancellationToken ct = default) =>
        await db.Seasons.AsNoTracking().OrderByDescending(s => s.IsCurrent).ThenByDescending(s => s.Year)
            .Select(s => new SeasonVm(s.Id, s.Year, s.Name)).FirstOrDefaultAsync(ct);

    public Task<List<CompetitionVm>> CompetitionsAsync(int seasonId, CancellationToken ct = default) =>
        db.Competitions.AsNoTracking().Where(c => c.SeasonId == seasonId && c.IsPublished).OrderBy(c => c.Order)
            .Select(c => new CompetitionVm(c.Id, c.Name, c.ShortName, c.Slug, c.Color)).ToListAsync(ct);

    private IQueryable<Match> Season(int seasonId, int? competitionId) =>
        db.Matches.Where(m => m.Phase.Competition.SeasonId == seasonId && m.Phase.Competition.IsPublished
            && (competitionId == null || m.Phase.CompetitionId == competitionId));

    /// <summary>Matchs d'une journée, regroupés par compétition puis par poule/tour. Les équipes suivies passent en premier.</summary>
    public async Task<List<MatchGroupVm>> DayAsync(int seasonId, DateOnly day, int? competitionId, IReadOnlySet<int> favorites,
        CancellationToken ct = default)
    {
        var from = KokoraTime.StartOfDayUtc(day);
        var to = KokoraTime.StartOfDayUtc(day.AddDays(1));
        var matches = await Season(seasonId, competitionId).Where(m => m.KickoffAt >= from && m.KickoffAt < to)
            .WithRowData().ToListAsync(ct);
        return Group(matches, favorites);
    }

    public async Task<List<MatchRowVm>> LiveAsync(int seasonId, int? competitionId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return (await Season(seasonId, competitionId).Where(m => LiveStatuses.Contains(m.Status)).WithRowData()
            .OrderBy(m => m.KickoffAt).ToListAsync(ct)).Select(m => Mapping.Row(m, now)).ToList();
    }

    /// <summary>Jours qui ont des matchs autour d'une date (pour la bande de dates).</summary>
    public async Task<List<DayInfo>> DaysAsync(int seasonId, int? competitionId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = KokoraTime.StartOfDayUtc(from);
        var end = KokoraTime.StartOfDayUtc(to.AddDays(1));
        var rows = await Season(seasonId, competitionId).Where(m => m.KickoffAt >= start && m.KickoffAt < end)
            .Select(m => new { m.KickoffAt, m.Status }).ToListAsync(ct);
        return rows.GroupBy(r => DateOnly.FromDateTime(KokoraTime.ToLocal(r.KickoffAt!.Value).DateTime))
            .Select(g => new DayInfo(g.Key, g.Count(), g.Any(r => LiveStatuses.Contains(r.Status))))
            .OrderBy(d => d.Day).ToList();
    }

    /// <summary>À venir (ordre chronologique) ou résultats (du plus récent au plus ancien), par pages.</summary>
    public async Task<(List<(DateOnly Day, List<MatchGroupVm> Groups)> Days, bool HasMore)> ListAsync(int seasonId, int? competitionId,
        bool upcoming, int page, int pageSize, IReadOnlySet<int> favorites, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = Season(seasonId, competitionId).Where(m => m.KickoffAt != null);
        q = upcoming
            ? q.Where(m => (m.Status == MatchStatus.Scheduled || m.Status == MatchStatus.Postponed) && m.KickoffAt >= now.AddHours(-3))
                .OrderBy(m => m.KickoffAt).ThenBy(m => m.Id)
            : q.Where(m => Played.Contains(m.Status)).OrderByDescending(m => m.KickoffAt).ThenBy(m => m.Id);
        var matches = await q.Skip((page - 1) * pageSize).Take(pageSize + 1).WithRowData().ToListAsync(ct);
        var hasMore = matches.Count > pageSize;
        var days = matches.Take(pageSize)
            .GroupBy(m => DateOnly.FromDateTime(KokoraTime.ToLocal(m.KickoffAt!.Value).DateTime))
            .Select(g => (g.Key, Group(g.ToList(), favorites))).ToList();
        return (days, hasMore);
    }

    /// <summary>Prochain match à l'affiche (marqué par l'admin), à défaut rien.</summary>
    public async Task<MatchRowVm?> NextFeaturedAsync(int seasonId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var m = await Season(seasonId, null)
            .Where(m => m.IsFeatured && m.KickoffAt > now.AddHours(-2) && (m.Status == MatchStatus.Scheduled || LiveStatuses.Contains(m.Status)))
            .OrderBy(m => m.KickoffAt).WithRowData().FirstOrDefaultAsync(ct);
        return m is null ? null : Mapping.Row(m, now);
    }

    public async Task<MatchDetailVm?> DetailAsync(int id, CancellationToken ct = default)
    {
        var m = await db.Matches.Where(x => x.Id == id && x.Phase.Competition.IsPublished)
            .Include(x => x.Referee)
            .Include(x => x.Events.Where(e => !e.IsCancelled)).ThenInclude(e => e.Player)
            .Include(x => x.Events).ThenInclude(e => e.AssistPlayer)
            .Include(x => x.Events).ThenInclude(e => e.PlayerOut)
            .Include(x => x.Lineups).ThenInclude(l => l.Player)
            .WithRowData().AsSplitQuery().FirstOrDefaultAsync(ct);
        if (m is null) return null;

        var now = DateTimeOffset.UtcNow;
        var row = Mapping.Row(m, now);
        var comp = m.Phase.Competition;
        var events = m.Events
            .Where(e => e.Type is not (MatchEventType.PeriodStart or MatchEventType.PeriodEnd or MatchEventType.Info))
            .OrderBy(e => e.Period).ThenBy(e => e.Minute).ThenBy(e => e.AddedTime ?? 0).ThenBy(e => e.Id)
            .Select(e => new TimelineEventVm(e.Id, e.Type, e.MinuteLabel, e.ClubId == m.HomeClubId,
                e.Player?.DisplayName, e.Player?.Slug, e.AssistPlayer?.DisplayName, e.PlayerOut?.DisplayName, e.IsScored, e.Period))
            .ToList();

        List<LineupPlayerVm> Lineup(int? clubId) => m.Lineups.Where(l => l.ClubId == clubId)
            .OrderByDescending(l => l.IsStarter).ThenBy(l => l.Player.Position).ThenBy(l => l.ShirtNumber)
            .Select(l => new LineupPlayerVm(l.Player.DisplayName, l.Player.Slug, l.ShirtNumber, l.IsCaptain, l.IsStarter, l.Player.Position))
            .ToList();

        int Count(int? clubId, params MatchEventType[] types) => m.Events.Count(e => !e.IsCancelled && e.ClubId == clubId && types.Contains(e.Type));

        var h2h = new List<MatchRowVm>();
        if (m.HomeClubId is { } h && m.AwayClubId is { } a)
        {
            h2h = (await db.Matches.Where(x => x.Id != m.Id && Played.Contains(x.Status)
                    && ((x.HomeClubId == h && x.AwayClubId == a) || (x.HomeClubId == a && x.AwayClubId == h)))
                .OrderByDescending(x => x.KickoffAt).Take(10).WithRowData().ToListAsync(ct))
                .Select(x => Mapping.Row(x, now)).ToList();
        }
        var summary = (
            h2h.Count(x => x.Outcome != 0 && (x.Outcome == 1 ? x.Home?.Id : x.Away?.Id) == m.HomeClubId),
            h2h.Count(x => x.ShowScore && x.Outcome == 0),
            h2h.Count(x => x.Outcome != 0 && (x.Outcome == 1 ? x.Home?.Id : x.Away?.Id) == m.AwayClubId));

        return new MatchDetailVm
        {
            Match = row,
            Competition = new CompetitionVm(comp.Id, comp.Name, comp.ShortName, comp.Slug, comp.Color),
            PhaseName = m.Phase.Name,
            Referee = m.Referee?.FullName,
            Notes = m.Notes,
            Events = events,
            HomeLineup = Lineup(m.HomeClubId),
            AwayLineup = Lineup(m.AwayClubId),
            HeadToHead = h2h,
            HeadToHeadSummary = summary,
            HomeYellow = Count(m.HomeClubId, MatchEventType.YellowCard),
            AwayYellow = Count(m.AwayClubId, MatchEventType.YellowCard),
            HomeRed = Count(m.HomeClubId, MatchEventType.RedCard, MatchEventType.SecondYellow),
            AwayRed = Count(m.AwayClubId, MatchEventType.RedCard, MatchEventType.SecondYellow),
            HomeHalfTime = m.HomeHalfTimeScore,
            AwayHalfTime = m.AwayHalfTimeScore
        };
    }

    /// <summary>Regroupe par compétition puis par poule/tour ; les groupes avec une équipe suivie en premier.</summary>
    private static List<MatchGroupVm> Group(List<Match> matches, IReadOnlySet<int> favorites)
    {
        var now = DateTimeOffset.UtcNow;
        return matches
            // Par identifiants : en lecture sans suivi, chaque match a sa propre instance de compétition.
            .GroupBy(m => (CompId: m.Phase.CompetitionId, m.PhaseId, m.GroupId, m.RoundId))
            .Select(g =>
            {
                var comp = g.First().Phase.Competition;
                var rows = g.OrderByDescending(m => m.IsLive).ThenBy(m => m.KickoffAt).ThenBy(m => m.Id)
                    .Select(m => Mapping.Row(m, now))
                    .OrderByDescending(r => r.Involves(favorites)).ToList();
                var first = g.First();
                var subtitle = first.Round?.Name ?? first.Group?.Name ?? first.Phase.Name;
                if (first.Round is null && g.Select(m => m.Matchday).Distinct().Count() == 1 && first.Matchday is { } md)
                    subtitle += $" · {md}{(md == 1 ? "re" : "e")} journée";
                return new
                {
                    Vm = new MatchGroupVm(comp.Name, subtitle, comp.Color, $"/classements/{comp.Slug}", rows),
                    Fav = rows.Any(r => r.Involves(favorites)),
                    Live = rows.Any(r => r.IsLive),
                    comp.Order,
                    PhaseOrder = first.Phase.Order,
                    Sub = first.Group?.Order ?? first.Round?.Order ?? 0
                };
            })
            .OrderByDescending(x => x.Fav).ThenByDescending(x => x.Live).ThenBy(x => x.Order).ThenBy(x => x.PhaseOrder).ThenBy(x => x.Sub)
            .Select(x => x.Vm).ToList();
    }
}
