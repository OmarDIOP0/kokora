using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Clubs;
using Kokora.Domain.Enums;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Public;

/// <summary>
/// Statistiques d'une compétition (ou de toute la saison) : buteurs, passeurs, équipes, fair-play, suspensions.
/// Seuls les matchs réellement joués comptent (les buts d'un forfait ou d'un match arrêté sont exclus).
/// </summary>
public class StatsService(IAppDbContext db, CompetitionCache cache)
{
    public static readonly MatchStatus[] RealMatches = [MatchStatus.Finished, MatchStatus.UnderReview];
    public static readonly MatchEventType[] GoalTypes = [MatchEventType.Goal, MatchEventType.PenaltyGoal];
    public static readonly MatchEventType[] CardTypes = [MatchEventType.YellowCard, MatchEventType.SecondYellow, MatchEventType.RedCard];
    /// <summary>Matchs qui permettent de purger une suspension (le forfait compte comme un match joué).</summary>
    private static readonly MatchStatus[] ServingMatches = [MatchStatus.Finished, MatchStatus.UnderReview, MatchStatus.Forfeit];

    /// <summary>Statistiques mises en cache (clé 0 = toute la saison).</summary>
    public Task<StatsPageData> GetAsync(int seasonId, int? competitionId, CancellationToken ct = default) =>
        cache.GetOrCreateAsync(competitionId ?? CompetitionCache.SeasonWide, $"stats:{seasonId}", () => ComputeAsync(seasonId, competitionId, ct));

    private async Task<StatsPageData> ComputeAsync(int seasonId, int? competitionId, CancellationToken ct)
    {
        var comps = await db.Competitions.AsNoTracking()
            .Where(c => c.SeasonId == seasonId && c.IsPublished && (competitionId == null || c.Id == competitionId))
            .ToListAsync(ct);
        var compIds = comps.Select(c => c.Id).ToList();

        var events = await db.MatchEvents.AsNoTracking()
            .Where(e => !e.IsCancelled && compIds.Contains(e.Match.Phase.CompetitionId) && RealMatches.Contains(e.Match.Status))
            .Select(e => new { e.Type, e.PlayerId, e.AssistPlayerId, e.ClubId })
            .ToListAsync(ct);

        var goals = events.Where(e => GoalTypes.Contains(e.Type) && e.PlayerId != null).ToList();
        var goalCounts = goals.GroupBy(e => (e.PlayerId!.Value, e.ClubId)).ToDictionary(g => g.Key, g => (g.Count(), g.Count(e => e.Type == MatchEventType.PenaltyGoal)));
        var assistCounts = goals.Where(e => e.AssistPlayerId != null).GroupBy(e => (e.AssistPlayerId!.Value, e.ClubId)).ToDictionary(g => g.Key, g => g.Count());

        var keys = goalCounts.Keys.Union(assistCounts.Keys).ToList();
        var players = await PlayersAsync(keys.Select(k => k.Item1).Distinct().ToList(), ct);
        var clubs = await ClubsAsync(keys.Select(k => k.ClubId ?? 0).Where(id => id > 0).Distinct().ToList(), ct);

        var rows = keys.Where(k => players.ContainsKey(k.Item1)).Select(k =>
        {
            var (g, p) = goalCounts.GetValueOrDefault(k);
            return new PlayerStatRow(players[k.Item1], k.ClubId is { } c ? clubs.GetValueOrDefault(c) : null, g, p, assistCounts.GetValueOrDefault(k));
        }).ToList();

        var teams = await TeamRowsAsync(compIds, ct);
        var (suspended, threatened) = await DisciplineAsync(comps.Select(c => (c.Id, c.Name, c.Suspensions)).ToList(), ct);

        return new StatsPageData(
            rows.Where(r => r.Goals > 0).OrderByDescending(r => r.Goals).ThenBy(r => r.Penalties).ThenByDescending(r => r.Assists).ThenBy(r => r.Player.Name).ToList(),
            rows.Where(r => r.Assists > 0).OrderByDescending(r => r.Assists).ThenByDescending(r => r.Goals).ThenBy(r => r.Player.Name).ToList(),
            rows.Where(r => r.Contributions > 0).OrderByDescending(r => r.Contributions).ThenByDescending(r => r.Goals).ThenBy(r => r.Player.Name).ToList(),
            teams,
            suspended, threatened,
            teams.Sum(t => t.Played) / 2,
            teams.Sum(t => t.GoalsFor));
    }

    /// <summary>Totaux par équipe (matchs joués sur le terrain) et discipline.</summary>
    public async Task<List<TeamStatRow>> TeamRowsAsync(IReadOnlyCollection<int> compIds, CancellationToken ct, int? onlyClub = null)
    {
        var matches = await db.Matches.AsNoTracking()
            .Where(m => compIds.Contains(m.Phase.CompetitionId) && RealMatches.Contains(m.Status)
                && m.HomeClubId != null && m.AwayClubId != null && m.HomeScore != null && m.AwayScore != null
                && (onlyClub == null || m.HomeClubId == onlyClub || m.AwayClubId == onlyClub))
            .Select(m => new { Home = m.HomeClubId!.Value, Away = m.AwayClubId!.Value, Hs = m.HomeScore!.Value, As = m.AwayScore!.Value })
            .ToListAsync(ct);
        var cards = await db.MatchEvents.AsNoTracking()
            .Where(e => !e.IsCancelled && e.ClubId != null && CardTypes.Contains(e.Type) && compIds.Contains(e.Match.Phase.CompetitionId)
                && (onlyClub == null || e.ClubId == onlyClub))
            .Select(e => new { Club = e.ClubId!.Value, e.Type }).ToListAsync(ct);
        var rules = (await db.Competitions.AsNoTracking().Where(c => compIds.Contains(c.Id)).Select(c => c.Scoring).FirstOrDefaultAsync(ct)) ?? new ScoringRules();

        var lines = matches.SelectMany(m => new[] { (Club: m.Home, Gf: m.Hs, Ga: m.As), (Club: m.Away, Gf: m.As, Ga: m.Hs) }).ToList();
        var ids = lines.Select(l => l.Club).Union(cards.Select(c => c.Club)).Where(id => onlyClub == null || id == onlyClub).Distinct().ToList();
        var clubs = await ClubsAsync(ids, ct);
        return ids.Where(clubs.ContainsKey).Select(id =>
        {
            var l = lines.Where(x => x.Club == id).ToList();
            var c = cards.Where(x => x.Club == id).ToList();
            int y = c.Count(x => x.Type == MatchEventType.YellowCard), y2 = c.Count(x => x.Type == MatchEventType.SecondYellow), r = c.Count(x => x.Type == MatchEventType.RedCard);
            return new TeamStatRow(clubs[id], l.Count, l.Sum(x => x.Gf), l.Sum(x => x.Ga), l.Count(x => x.Ga == 0), y, y2, r,
                y * rules.FairPlayYellowPoints + y2 * rules.FairPlaySecondYellowPoints + r * rules.FairPlayRedPoints);
        }).ToList();
    }

    /// <summary>Suspensions (automatiques + commission) et joueurs à un carton de la suspension.</summary>
    public async Task<(List<SuspensionRowVm> Suspended, List<ThreatenedVm> Threatened)> DisciplineAsync(
        IReadOnlyList<(int Id, string Name, SuspensionRules Rules)> comps, CancellationToken ct, bool includeServed = false)
    {
        var suspended = new List<SuspensionRowVm>();
        var threatened = new List<ThreatenedVm>();
        foreach (var comp in comps)
        {
            var report = await ReportAsync(comp.Id, comp.Rules, ct);
            var list = includeServed ? report.Suspensions : report.Active;
            var threshold = comp.Rules.YellowCardsThreshold;
            var atRisk = comp.Rules.Enabled && threshold > 1
                ? report.PendingYellows.Where(p => p.Value == threshold - 1).ToList() : [];
            var playerIds = list.Select(s => s.PlayerId).Union(atRisk.Select(p => p.Key)).Distinct().ToList();
            if (playerIds.Count == 0) continue;
            var players = await PlayersAsync(playerIds, ct);
            var lastClub = await LastClubAsync(comp.Id, atRisk.Select(p => p.Key).ToList(), ct);
            var clubs = await ClubsAsync(list.Select(s => s.ClubId).Union(lastClub.Values).Distinct().ToList(), ct);
            suspended.AddRange(list.Where(s => players.ContainsKey(s.PlayerId) && clubs.ContainsKey(s.ClubId)).Select(s =>
                new SuspensionRowVm(players[s.PlayerId], clubs[s.ClubId], comp.Name, s.Reason, s.Detail, s.Matches, s.Remaining, s.TriggeredAt)));
            threatened.AddRange(atRisk.Where(p => players.ContainsKey(p.Key) && lastClub.ContainsKey(p.Key) && clubs.ContainsKey(lastClub[p.Key]))
                .Select(p => new ThreatenedVm(players[p.Key], clubs[lastClub[p.Key]], comp.Name, p.Value, threshold)));
        }
        return (suspended.OrderByDescending(s => s.TriggeredAt).ToList(), threatened.OrderBy(t => t.Team.Name).ThenBy(t => t.Player.Name).ToList());
    }

    /// <summary>Rapport de discipline brut d'une compétition.</summary>
    public async Task<DisciplineReport> ReportAsync(int competitionId, SuspensionRules rules, CancellationToken ct)
    {
        var cards = await db.MatchEvents.AsNoTracking()
            .Where(e => !e.IsCancelled && e.PlayerId != null && e.ClubId != null && CardTypes.Contains(e.Type)
                && e.Match.Phase.CompetitionId == competitionId && ServingMatches.Contains(e.Match.Status))
            .Select(e => new CardRecord(e.PlayerId!.Value, e.ClubId!.Value, e.MatchId, e.Match.PhaseId,
                e.Match.KickoffAt ?? DateTimeOffset.MinValue, e.Type))
            .ToListAsync(ct);
        var played = (await db.Matches.AsNoTracking()
                .Where(m => m.Phase.CompetitionId == competitionId && ServingMatches.Contains(m.Status) && m.KickoffAt != null)
                .Select(m => new { m.Id, m.HomeClubId, m.AwayClubId, m.KickoffAt }).ToListAsync(ct))
            .SelectMany(m => new[] { (Club: m.HomeClubId, m.Id, m.KickoffAt), (Club: m.AwayClubId, m.Id, m.KickoffAt) })
            .Where(x => x.Club != null)
            .GroupBy(x => x.Club!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.Id, x.KickoffAt!.Value)).ToList());
        var manual = await db.Suspensions.AsNoTracking()
            .Where(s => s.CompetitionId == competitionId && s.ClubId != null)
            .Select(s => new { s.PlayerId, ClubId = s.ClubId!.Value, s.Matches, s.DecidedOn, s.Reason,
                TriggerAt = s.TriggerMatch != null ? s.TriggerMatch.KickoffAt : null })
            .ToListAsync(ct);
        return SuspensionCalculator.Compute(rules, cards, played, manual.Select(m => new ManualSuspension(m.PlayerId, m.ClubId, m.Matches,
            m.TriggerAt ?? KokoraTime.StartOfDayUtc(m.DecidedOn), m.Reason)));
    }

    private async Task<Dictionary<int, int>> LastClubAsync(int competitionId, List<int> playerIds, CancellationToken ct) =>
        playerIds.Count == 0 ? [] : (await db.MatchEvents.AsNoTracking()
                .Where(e => e.PlayerId != null && playerIds.Contains(e.PlayerId.Value) && e.ClubId != null && e.Match.Phase.CompetitionId == competitionId)
                .OrderByDescending(e => e.Match.KickoffAt)
                .Select(e => new { Player = e.PlayerId!.Value, Club = e.ClubId!.Value }).ToListAsync(ct))
            .GroupBy(x => x.Player).ToDictionary(g => g.Key, g => g.First().Club);

    internal async Task<Dictionary<int, PlayerVm>> PlayersAsync(List<int> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await db.Players.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct))
            .ToDictionary(p => p.Id, p => ToVm(p));

    internal async Task<Dictionary<int, TeamVm>> ClubsAsync(List<int> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await db.Clubs.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => Mapping.Team(c)!);

    public static PlayerVm ToVm(Player p, int? number = null) =>
        new(p.Id, p.DisplayName, p.Slug, p.PhotoPath, p.Position, number);
}
