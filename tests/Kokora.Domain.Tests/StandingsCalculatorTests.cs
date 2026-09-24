using FluentAssertions;
using Kokora.Domain.Enums;
using Kokora.Domain.Rules;

namespace Kokora.Domain.Tests;

public class StandingsCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 16, 0, 0, TimeSpan.Zero);
    private static int _day;
    private static GameResult G(int home, int away, int hg, int ag, int? forfeit = null) =>
        new(home, away, hg, ag, T0.AddDays(Interlocked.Increment(ref _day)), forfeit);

    private static ScoringRules Rules(params TieBreaker[] order) =>
        new() { TieBreakers = order.Length == 0 ? [TieBreaker.GoalDifference, TieBreaker.GoalsFor, TieBreaker.HeadToHead, TieBreaker.FairPlay] : [.. order] };

    [Fact]
    public void Counts_points_goals_and_results()
    {
        var table = StandingsCalculator.Compute([1, 2, 3], [G(1, 2, 2, 0), G(2, 3, 1, 1), G(3, 1, 0, 1)], Rules());

        // 2 et 3 à 1 pt : 3 a la meilleure différence (-1 contre -2).
        table.Select(r => r.ClubId).Should().Equal(1, 3, 2);
        var first = table[0];
        (first.Played, first.Won, first.Drawn, first.Lost, first.GoalsFor, first.GoalsAgainst, first.Points)
            .Should().Be((2, 2, 0, 0, 3, 0, 6));
        table[1].Points.Should().Be(1);
        table[2].Points.Should().Be(1);
        table.Select(r => r.Rank).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Team_without_match_appears_with_zero()
    {
        var table = StandingsCalculator.Compute([1, 2, 9], [G(1, 2, 1, 0)], Rules());
        table.Should().Contain(r => r.ClubId == 9 && r.Played == 0 && r.Points == 0);
    }

    [Fact]
    public void Forfeit_applies_the_administrative_score_and_points()
    {
        var rules = Rules();
        rules.PointsForForfeitLoss = -1;
        // Le score saisi (0-0) est ignoré : 3-0 pour l'adversaire, -1 pour l'équipe forfait.
        var table = StandingsCalculator.Compute([1, 2], [G(1, 2, 0, 0, forfeit: 2)], rules);

        var winner = table.Single(r => r.ClubId == 1);
        var loser = table.Single(r => r.ClubId == 2);
        (winner.Points, winner.GoalsFor, winner.GoalsAgainst).Should().Be((3, 3, 0));
        (loser.Points, loser.Lost, loser.GoalsAgainst).Should().Be((-1, 1, 3));
        loser.Form.Should().Equal('D');
    }

    [Fact]
    public void Point_penalty_is_applied_and_can_change_the_order()
    {
        var table = StandingsCalculator.Compute([1, 2], [G(1, 2, 1, 0), G(2, 1, 1, 0)], Rules(),
            pointAdjustments: new Dictionary<int, int> { [1] = -2 });
        table[0].ClubId.Should().Be(2);
        table[1].Points.Should().Be(1);
        table[1].Adjustment.Should().Be(-2);
    }

    [Fact]
    public void Goal_difference_then_goals_for_break_ties()
    {
        // 1 et 2 : 3 pts chacun ; 1 a +3, 2 a +1.
        var table = StandingsCalculator.Compute([1, 2, 3, 4], [G(1, 3, 3, 0), G(2, 4, 1, 0), G(3, 2, 0, 0), G(4, 1, 0, 0)], Rules());
        table[0].ClubId.Should().Be(1);

        // Même différence (+2), plus de buts marqués pour 2.
        var byGoals = StandingsCalculator.Compute([1, 2, 3, 4], [G(1, 3, 2, 0), G(2, 4, 3, 1)], Rules());
        byGoals[0].ClubId.Should().Be(2);
        byGoals[1].ClubId.Should().Be(1);
    }

    [Fact]
    public void Head_to_head_uses_a_mini_league_between_tied_teams_only()
    {
        // Ordre : points puis confrontation directe (comme dans beaucoup de règlements navétane).
        var rules = Rules(TieBreaker.HeadToHead, TieBreaker.GoalDifference);
        // 1, 2, 3 à 6 pts. Entre eux : 1 bat 2, 2 bat 3, 3 bat 1 (triangle) → mini-classement à égalité de points,
        // départagé par la différence dans les confrontations : 3 a gagné 3-0 contre 1.
        var results = new[]
        {
            G(1, 2, 1, 0), G(2, 3, 1, 0), G(3, 1, 3, 0),
            G(1, 4, 5, 0), G(2, 4, 1, 0), G(3, 4, 1, 0),
        };
        var table = StandingsCalculator.Compute([1, 2, 3, 4], results, rules);
        table.Select(r => r.ClubId).Should().Equal(3, 2, 1, 4);
        // Au classement général, 1 a pourtant la meilleure différence (+3) : la confrontation directe passe avant.
        table.Single(r => r.ClubId == 1).GoalDifference.Should().Be(3);
    }

    [Fact]
    public void Head_to_head_between_two_teams_decides_before_goal_difference_when_configured()
    {
        var rules = Rules(TieBreaker.HeadToHead, TieBreaker.GoalDifference);
        var table = StandingsCalculator.Compute([1, 2, 3], [G(2, 1, 1, 0), G(1, 3, 5, 0), G(3, 2, 1, 0)], rules);
        // Les trois équipes à 3 pts (triangle) : différence dans les confrontations 1 : +4, 2 : 0, 3 : -4.
        table[0].ClubId.Should().Be(1);

        var direct = StandingsCalculator.Compute([1, 2, 3], [G(2, 1, 1, 0), G(1, 3, 4, 0), G(2, 3, 0, 1), G(3, 1, 0, 0)],
            Rules(TieBreaker.HeadToHead, TieBreaker.GoalDifference));
        // 1 et 3 à 4 pts, 2 à 3 pts. Entre 1 et 3 : 4-0 puis 0-0 → 1 devant.
        direct[0].ClubId.Should().Be(1);
    }

    [Fact]
    public void Fair_play_breaks_a_perfect_tie()
    {
        var table = StandingsCalculator.Compute([1, 2], [G(1, 2, 1, 1)], Rules(),
            disciplinePoints: new Dictionary<int, int> { [1] = 5, [2] = 1 });
        table[0].ClubId.Should().Be(2);
    }

    [Fact]
    public void Configured_order_is_respected()
    {
        // Même points ; 1 a la meilleure différence (+2), 2 a plus de buts marqués (4).
        var results = new[] { G(1, 3, 2, 0), G(2, 4, 4, 3), G(3, 4, 0, 0) };
        StandingsCalculator.Compute([1, 2, 3, 4], results, Rules(TieBreaker.GoalDifference, TieBreaker.GoalsFor))[0].ClubId.Should().Be(1);
        StandingsCalculator.Compute([1, 2, 3, 4], results, Rules(TieBreaker.GoalsFor, TieBreaker.GoalDifference))[0].ClubId.Should().Be(2);
    }

    [Fact]
    public void Form_keeps_the_last_five_results_in_chronological_order()
    {
        var results = new List<GameResult>();
        int[] outcomes = [1, 1, 0, -1, 1, -1, 0]; // V V N D V D N
        foreach (var o in outcomes)
            results.Add(G(1, 2, o > 0 ? 1 : 0, o < 0 ? 1 : 0));
        var row = StandingsCalculator.Compute([1, 2], results, Rules()).Single(r => r.ClubId == 1);
        row.Form.Should().Equal('N', 'D', 'V', 'D', 'N');
        row.Played.Should().Be(7);
    }

    [Fact]
    public void Results_involving_teams_outside_the_group_are_ignored()
    {
        var table = StandingsCalculator.Compute([1, 2], [G(1, 99, 5, 0), G(1, 2, 0, 1)], Rules());
        table.Single(r => r.ClubId == 1).Played.Should().Be(1);
    }

    [Fact]
    public void Discipline_weights_follow_the_rules()
    {
        var rules = new ScoringRules();
        StandingsCalculator.DisciplineWeight(MatchEventType.YellowCard, rules).Should().Be(1);
        StandingsCalculator.DisciplineWeight(MatchEventType.SecondYellow, rules).Should().Be(3);
        StandingsCalculator.DisciplineWeight(MatchEventType.RedCard, rules).Should().Be(4);
        StandingsCalculator.DisciplineWeight(MatchEventType.Goal, rules).Should().Be(0);
    }
}
