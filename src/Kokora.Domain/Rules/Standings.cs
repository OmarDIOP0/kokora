using Kokora.Domain.Enums;

namespace Kokora.Domain.Rules;

/// <summary>Résultat d'un match comptant pour le classement.</summary>
/// <param name="ForfeitingClubId">Équipe déclarée forfait (score administratif appliqué selon les règles).</param>
public readonly record struct GameResult(
    int HomeId, int AwayId, int HomeGoals, int AwayGoals, DateTimeOffset PlayedAt, int? ForfeitingClubId = null);

public record StandingRow
{
    public int ClubId { get; init; }
    public int Rank { get; set; }
    public int Played { get; set; }
    public int Won { get; set; }
    public int Drawn { get; set; }
    public int Lost { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int Points { get; set; }
    /// <summary>Pénalités ou bonus de points de la commission (déjà inclus dans Points).</summary>
    public int Adjustment { get; set; }
    /// <summary>Points de discipline (plus bas = plus fair-play).</summary>
    public int DisciplinePoints { get; set; }
    /// <summary>Derniers résultats, du plus ancien au plus récent : 'V', 'N', 'D'.</summary>
    public List<char> Form { get; } = [];
    public int GoalDifference => GoalsFor - GoalsAgainst;
}

/// <summary>
/// Calcule le classement d'une poule : points (victoire, nul, défaite, forfait), pénalités,
/// puis les critères de départage dans l'ordre configuré. La confrontation directe se calcule
/// sur un mini-classement entre les seules équipes encore à égalité.
/// </summary>
public static class StandingsCalculator
{
    public const int FormLength = 5;

    public static List<StandingRow> Compute(
        IReadOnlyCollection<int> clubIds,
        IReadOnlyCollection<GameResult> results,
        ScoringRules rules,
        IReadOnlyDictionary<int, int>? pointAdjustments = null,
        IReadOnlyDictionary<int, int>? disciplinePoints = null)
    {
        var rows = clubIds.Distinct().ToDictionary(id => id, id => new StandingRow { ClubId = id });

        foreach (var r in results.OrderBy(r => r.PlayedAt))
        {
            if (!rows.TryGetValue(r.HomeId, out var home) || !rows.TryGetValue(r.AwayId, out var away)) continue;
            var (hg, ag) = Goals(r, rules);
            Apply(home, hg, ag, rules, forfeited: r.ForfeitingClubId == r.HomeId);
            Apply(away, ag, hg, rules, forfeited: r.ForfeitingClubId == r.AwayId);
        }

        foreach (var row in rows.Values)
        {
            if (pointAdjustments?.TryGetValue(row.ClubId, out var adj) == true)
            {
                row.Adjustment = adj;
                row.Points += adj;
            }
            if (disciplinePoints?.TryGetValue(row.ClubId, out var dp) == true) row.DisciplinePoints = dp;
            if (row.Form.Count > FormLength) row.Form.RemoveRange(0, row.Form.Count - FormLength);
        }

        var criteria = new List<TieBreaker>(rules.TieBreakers.Distinct());
        var ordered = Rank(rows.Values.ToList(), criteria, 0, results, rules);
        for (var i = 0; i < ordered.Count; i++) ordered[i].Rank = i + 1;
        return ordered;
    }

    /// <summary>Score retenu : pour un forfait, le score administratif des règles.</summary>
    public static (int Home, int Away) Goals(GameResult r, ScoringRules rules) =>
        r.ForfeitingClubId == r.HomeId ? (rules.ForfeitGoalsAgainst, rules.ForfeitGoalsFor)
        : r.ForfeitingClubId == r.AwayId ? (rules.ForfeitGoalsFor, rules.ForfeitGoalsAgainst)
        : (r.HomeGoals, r.AwayGoals);

    private static void Apply(StandingRow row, int gf, int ga, ScoringRules rules, bool forfeited)
    {
        row.Played++;
        row.GoalsFor += gf;
        row.GoalsAgainst += ga;
        if (gf > ga)
        {
            row.Won++;
            row.Points += rules.PointsForWin;
            row.Form.Add('V');
        }
        else if (gf == ga)
        {
            row.Drawn++;
            row.Points += rules.PointsForDraw;
            row.Form.Add('N');
        }
        else
        {
            row.Lost++;
            row.Points += forfeited ? rules.PointsForForfeitLoss : rules.PointsForLoss;
            row.Form.Add('D');
        }
    }

    // Partition récursive : on trie sur le critère courant, puis on départage chaque groupe d'égalité
    // avec les critères suivants. Le critère 0 (implicite) est le nombre de points.
    private static List<StandingRow> Rank(List<StandingRow> teams, List<TieBreaker> criteria, int level,
        IReadOnlyCollection<GameResult> results, ScoringRules rules)
    {
        if (teams.Count <= 1) return teams;
        if (level > criteria.Count)
            return teams.OrderBy(t => t.ClubId).ToList(); // égalité parfaite : ordre stable (décision de la commission)

        Func<StandingRow, IComparable> key = level == 0
            ? t => t.Points
            : KeyFor(criteria[level - 1], teams, results, rules);

        var result = new List<StandingRow>(teams.Count);
        foreach (var group in teams.GroupBy(key).OrderByDescending(g => g.Key))
            result.AddRange(Rank(group.ToList(), criteria, level + 1, results, rules));
        return result;
    }

    private static Func<StandingRow, IComparable> KeyFor(TieBreaker criterion, List<StandingRow> tied,
        IReadOnlyCollection<GameResult> results, ScoringRules rules) => criterion switch
    {
        TieBreaker.GoalDifference => t => t.GoalDifference,
        TieBreaker.GoalsFor => t => t.GoalsFor,
        TieBreaker.Wins => t => t.Won,
        TieBreaker.FairPlay => t => -t.DisciplinePoints,
        TieBreaker.HeadToHead => HeadToHeadKey(tied, results, rules),
        _ => _ => 0
    };

    /// <summary>Mini-classement entre les équipes à égalité : points, différence, buts marqués.</summary>
    private static Func<StandingRow, IComparable> HeadToHeadKey(List<StandingRow> tied,
        IReadOnlyCollection<GameResult> results, ScoringRules rules)
    {
        var ids = tied.Select(t => t.ClubId).ToHashSet();
        var mini = tied.ToDictionary(t => t.ClubId, _ => (Pts: 0, Gf: 0, Ga: 0));
        foreach (var r in results.Where(r => ids.Contains(r.HomeId) && ids.Contains(r.AwayId)))
        {
            var (hg, ag) = Goals(r, rules);
            var h = mini[r.HomeId];
            var a = mini[r.AwayId];
            h.Gf += hg; h.Ga += ag; a.Gf += ag; a.Ga += hg;
            if (hg > ag) h.Pts += rules.PointsForWin;
            else if (hg < ag) a.Pts += rules.PointsForWin;
            else { h.Pts += rules.PointsForDraw; a.Pts += rules.PointsForDraw; }
            mini[r.HomeId] = h;
            mini[r.AwayId] = a;
        }
        return t => (mini[t.ClubId].Pts, mini[t.ClubId].Gf - mini[t.ClubId].Ga, mini[t.ClubId].Gf);
    }

    /// <summary>Points de discipline d'un événement de match pour le classement fair-play.</summary>
    public static int DisciplineWeight(MatchEventType type, ScoringRules rules) => type switch
    {
        MatchEventType.YellowCard => rules.FairPlayYellowPoints,
        MatchEventType.SecondYellow => rules.FairPlaySecondYellowPoints,
        MatchEventType.RedCard => rules.FairPlayRedPoints,
        _ => 0
    };
}
