using FluentAssertions;
using Kokora.Domain.Enums;
using Kokora.Domain.Rules;

namespace Kokora.Domain.Tests;

public class SuspensionCalculatorTests
{
    private const int Player = 7, Club = 1, Phase = 10;
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 16, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Day(int n) => T0.AddDays(n);

    private static CardRecord Card(int matchDay, MatchEventType type, int phase = Phase) =>
        new(Player, Club, 100 + matchDay, phase, Day(matchDay), type);

    /// <summary>L'équipe joue un match tous les jours (id 100 + jour).</summary>
    private static Dictionary<int, List<(int, DateTimeOffset)>> Played(int days) =>
        new() { [Club] = Enumerable.Range(1, days).Select(d => (100 + d, Day(d))).ToList() };

    [Fact]
    public void Third_yellow_triggers_one_match_and_the_counter_restarts()
    {
        var cards = new[] { Card(1, MatchEventType.YellowCard), Card(2, MatchEventType.YellowCard), Card(4, MatchEventType.YellowCard), Card(6, MatchEventType.YellowCard) };
        var report = SuspensionCalculator.Compute(new SuspensionRules(), cards, Played(10));

        var s = report.Suspensions.Should().ContainSingle().Subject;
        s.Reason.Should().Be(SuspensionReason.YellowAccumulation);
        s.TriggerMatchId.Should().Be(104);
        s.Served.Should().Be(1);
        report.PendingYellows[Player].Should().Be(1);
    }

    [Fact]
    public void Suspension_stays_active_until_the_team_has_played()
    {
        var cards = new[] { Card(1, MatchEventType.RedCard) };
        var rules = new SuspensionRules { MatchesForDirectRed = 2 };

        SuspensionCalculator.Compute(rules, cards, Played(1)).Active.Should().ContainSingle().Which.Remaining.Should().Be(2);
        SuspensionCalculator.Compute(rules, cards, Played(2)).Active.Single().Remaining.Should().Be(1);
        SuspensionCalculator.Compute(rules, cards, Played(3)).Active.Should().BeEmpty();
    }

    [Fact]
    public void Second_yellow_is_an_expulsion_and_its_yellows_do_not_accumulate()
    {
        var cards = new[]
        {
            Card(1, MatchEventType.YellowCard), Card(1, MatchEventType.YellowCard), Card(1, MatchEventType.SecondYellow),
            Card(3, MatchEventType.YellowCard),
        };
        var report = SuspensionCalculator.Compute(new SuspensionRules(), cards, Played(5));
        report.Suspensions.Should().ContainSingle().Which.Reason.Should().Be(SuspensionReason.SecondYellow);
        report.PendingYellows[Player].Should().Be(1);
    }

    [Fact]
    public void Consecutive_suspensions_are_served_one_after_the_other()
    {
        var cards = new[] { Card(1, MatchEventType.RedCard) };
        var manual = new[] { new ManualSuspension(Player, Club, 2, Day(1).AddHours(5), "Comportement") };
        var report = SuspensionCalculator.Compute(new SuspensionRules(), cards, Played(3), manual);
        // Rouge : match du jour 2 ; commission : jours 3 puis 4 (pas encore joué).
        report.Suspensions.Single(s => s.Reason == SuspensionReason.DirectRed).Served.Should().Be(1);
        var commission = report.Suspensions.Single(s => s.Reason == SuspensionReason.Commission);
        commission.Served.Should().Be(1);
        commission.Remaining.Should().Be(1);
        commission.Detail.Should().Be("Comportement");
    }

    [Fact]
    public void Yellows_can_reset_at_each_phase()
    {
        var cards = new[] { Card(1, MatchEventType.YellowCard), Card(2, MatchEventType.YellowCard), Card(3, MatchEventType.YellowCard, phase: 11) };
        SuspensionCalculator.Compute(new SuspensionRules(), cards, Played(5)).Suspensions.Should().ContainSingle();
        SuspensionCalculator.Compute(new SuspensionRules { ResetYellowsEachPhase = true }, cards, Played(5)).Suspensions.Should().BeEmpty();
    }

    [Fact]
    public void Disabled_rules_ignore_cards_but_keep_commission_decisions()
    {
        var cards = new[] { Card(1, MatchEventType.RedCard) };
        var manual = new[] { new ManualSuspension(Player, Club, 1, Day(0), "Réserve") };
        var report = SuspensionCalculator.Compute(new SuspensionRules { Enabled = false }, cards, Played(0), manual);
        report.Suspensions.Should().ContainSingle().Which.Reason.Should().Be(SuspensionReason.Commission);
    }

    [Fact]
    public void Yellow_before_a_direct_red_still_counts()
    {
        var cards = new[] { Card(1, MatchEventType.YellowCard), Card(2, MatchEventType.YellowCard), Card(2, MatchEventType.RedCard) };
        var report = SuspensionCalculator.Compute(new SuspensionRules(), cards, Played(5));
        report.Suspensions.Should().ContainSingle(s => s.Reason == SuspensionReason.DirectRed);
        report.PendingYellows[Player].Should().Be(2);
    }
}
