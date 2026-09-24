using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Public;

/// <summary>Classements des poules et tableaux à élimination, calculés à partir des résultats et mis en cache.</summary>
public class StandingsService(IAppDbContext db, CompetitionCache cache)
{
    /// <summary>Statuts dont le score compte au classement (un match « sous réserve » compte tant que la commission n'a pas tranché).</summary>
    public static readonly MatchStatus[] Counted = [MatchStatus.Finished, MatchStatus.Forfeit, MatchStatus.UnderReview];

    public async Task<List<PhaseStandingsVm>> CompetitionAsync(int competitionId, CancellationToken ct = default) =>
        await cache.GetOrCreateAsync(competitionId, "standings", async () =>
        {
            var phases = await db.Phases.AsNoTracking().Where(p => p.CompetitionId == competitionId)
                .OrderBy(p => p.Order).Select(p => p.Id).ToListAsync(ct);
            var list = new List<PhaseStandingsVm>();
            foreach (var id in phases) list.Add(await ComputePhaseAsync(id, ct));
            return list;
        });

    public async Task<PhaseStandingsVm?> PhaseAsync(int phaseId, CancellationToken ct = default)
    {
        var competitionId = await db.Phases.Where(p => p.Id == phaseId).Select(p => (int?)p.CompetitionId).FirstOrDefaultAsync(ct);
        if (competitionId is null) return null;
        return (await CompetitionAsync(competitionId.Value, ct)).FirstOrDefault(p => p.PhaseId == phaseId);
    }

    /// <summary>Classement brut d'une poule (utilisé pour générer les qualifications).</summary>
    public async Task<List<StandingRow>> GroupRowsAsync(int groupId, CancellationToken ct = default)
    {
        var group = await db.Groups.AsNoTracking().Include(g => g.Teams).Include(g => g.PointAdjustments)
            .Include(g => g.Phase).ThenInclude(p => p.Competition)
            .FirstOrDefaultAsync(g => g.Id == groupId, ct) ?? throw new NotFoundException("Poule");
        var (results, discipline) = await LoadResultsAsync(group.PhaseId, group.Phase.Competition.Scoring, ct);
        return Compute(group, results.GetValueOrDefault(group.Id) ?? [], discipline, group.Phase.Competition.Scoring);
    }

    private async Task<PhaseStandingsVm> ComputePhaseAsync(int phaseId, CancellationToken ct)
    {
        var phase = await db.Phases.AsNoTracking()
            .Include(p => p.Competition)
            .Include(p => p.Groups.OrderBy(g => g.Order)).ThenInclude(g => g.Teams).ThenInclude(t => t.Club)
            .Include(p => p.Groups).ThenInclude(g => g.PointAdjustments)
            .AsSplitQuery()
            .FirstAsync(p => p.Id == phaseId, ct);

        if (phase.Type == PhaseType.Knockout)
            return new PhaseStandingsVm(phase.Id, phase.Name, phase.Type, [], await BracketAsync(phase, ct));

        var rules = phase.Competition.Scoring;
        var (results, discipline) = await LoadResultsAsync(phaseId, rules, ct);
        var totals = await db.Matches.Where(m => m.PhaseId == phaseId && m.GroupId != null)
            .GroupBy(m => m.GroupId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var groups = new List<GroupStandingsVm>();
        foreach (var g in phase.Groups)
        {
            var groupResults = results.GetValueOrDefault(g.Id) ?? [];
            var rows = Compute(g, groupResults, discipline, rules);
            var clubs = g.Teams.ToDictionary(t => t.ClubId, t => t.Club);
            var table = new StandingsTableVm(
                $"{phase.Competition.Name} · {g.Name}",
                rows.Select(r => new StandingRowVm
                {
                    Rank = r.Rank, Team = Mapping.Team(clubs[r.ClubId])!, Played = r.Played, Won = r.Won, Drawn = r.Drawn,
                    Lost = r.Lost, GoalsFor = r.GoalsFor, GoalsAgainst = r.GoalsAgainst, Points = r.Points,
                    Adjustment = r.Adjustment, Form = r.Form.ToList(), Zone = ZoneOf(g, r.Rank)
                }).ToList(),
                g.Zones.Select(z => (Code(z.Kind), z.Label)).ToList());
            groups.Add(new GroupStandingsVm(g.Id, g.Name, table, groupResults.Count, totals.GetValueOrDefault(g.Id)));
        }
        return new PhaseStandingsVm(phase.Id, phase.Name, phase.Type, groups, null);
    }

    private static List<StandingRow> Compute(Group g, List<GameResult> results, Dictionary<int, int> discipline, ScoringRules rules) =>
        StandingsCalculator.Compute(
            g.Teams.Select(t => t.ClubId).ToList(), results, rules,
            g.PointAdjustments.GroupBy(a => a.ClubId).ToDictionary(x => x.Key, x => x.Sum(a => a.Points)),
            discipline);

    /// <summary>Résultats comptabilisés par poule + points de discipline par équipe sur la phase.</summary>
    private async Task<(Dictionary<int, List<GameResult>> Results, Dictionary<int, int> Discipline)> LoadResultsAsync(
        int phaseId, ScoringRules rules, CancellationToken ct)
    {
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.PhaseId == phaseId && m.GroupId != null && Counted.Contains(m.Status)
                && m.HomeClubId != null && m.AwayClubId != null && m.HomeScore != null && m.AwayScore != null)
            .Select(m => new { GroupId = m.GroupId!.Value, m.HomeClubId, m.AwayClubId, m.HomeScore, m.AwayScore, m.KickoffAt, m.ForfeitingClubId, m.Id })
            .ToListAsync(ct);

        var results = matches.GroupBy(m => m.GroupId).ToDictionary(g => g.Key, g => g.Select(m =>
            new GameResult(m.HomeClubId!.Value, m.AwayClubId!.Value, m.HomeScore!.Value, m.AwayScore!.Value,
                m.KickoffAt ?? DateTimeOffset.MinValue.AddDays(m.Id), m.ForfeitingClubId)).ToList());

        MatchEventType[] cards = [MatchEventType.YellowCard, MatchEventType.SecondYellow, MatchEventType.RedCard];
        var discipline = (await db.MatchEvents.AsNoTracking()
                .Where(e => e.Match.PhaseId == phaseId && !e.IsCancelled && e.ClubId != null && cards.Contains(e.Type))
                .Select(e => new { ClubId = e.ClubId!.Value, e.Type }).ToListAsync(ct))
            .GroupBy(e => e.ClubId)
            .ToDictionary(g => g.Key, g => g.Sum(e => StandingsCalculator.DisciplineWeight(e.Type, rules)));
        return (results, discipline);
    }

    private async Task<BracketVm> BracketAsync(Phase phase, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rounds = await db.Rounds.AsNoTracking().Where(r => r.PhaseId == phase.Id).OrderBy(r => r.Order).ToListAsync(ct);
        var matches = await db.Matches.Where(m => m.PhaseId == phase.Id && m.RoundId != null).WithRowData().ToListAsync(ct);
        return new BracketVm(rounds.Select(r => new BracketRoundVm(r.Name, r.Kind,
            matches.Where(m => m.RoundId == r.Id).OrderBy(m => m.BracketPosition ?? int.MaxValue).ThenBy(m => m.KickoffAt)
                .Select(m => Mapping.Row(m, now)).ToList())).ToList());
    }

    private static char? ZoneOf(Group g, int rank) =>
        g.Zones.FirstOrDefault(z => rank >= z.FromRank && rank <= z.ToRank) is { } z ? Code(z.Kind) : null;

    private static char Code(StandingZoneKind k) => k switch
    {
        StandingZoneKind.Qualified => 'q',
        StandingZoneKind.Playoff => 'p',
        _ => 'e'
    };
}
