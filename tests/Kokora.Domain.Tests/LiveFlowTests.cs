using FluentAssertions;
using Kokora.Domain.Enums;
using Kokora.Domain.Rules;

namespace Kokora.Domain.Tests;

public class LiveFlowTests
{
    private static readonly LiveContext League = new(false, false, false, true);
    private static readonly LiveContext KnockoutDraw = new(true, false, true, true);
    private static readonly LiveContext KnockoutDrawWithExtraTime = new(true, true, true, true);
    private static readonly LiveContext KnockoutWin = new(true, true, true, false);

    [Fact]
    public void Regular_match_goes_through_both_halves()
    {
        LiveFlow.Next(LivePeriod.NotStarted, League).Should().Equal(LivePeriod.FirstHalf);
        LiveFlow.Next(LivePeriod.FirstHalf, League).Should().Equal(LivePeriod.HalfTime);
        LiveFlow.Next(LivePeriod.HalfTime, League).Should().Equal(LivePeriod.SecondHalf);
        LiveFlow.Next(LivePeriod.SecondHalf, League).Should().Equal(LivePeriod.Ended);
        LiveFlow.Next(LivePeriod.Ended, League).Should().BeEmpty();
    }

    [Fact]
    public void Knockout_draw_goes_straight_to_penalties_by_default()
    {
        LiveFlow.Next(LivePeriod.SecondHalf, KnockoutDraw).Should().Equal(LivePeriod.Penalties);
        LiveFlow.Next(LivePeriod.Penalties, KnockoutDraw).Should().Equal(LivePeriod.Ended);
    }

    [Fact]
    public void Knockout_draw_with_extra_time_plays_it_before_penalties()
    {
        LiveFlow.Next(LivePeriod.SecondHalf, KnockoutDrawWithExtraTime).Should().Equal(LivePeriod.BreakBeforeExtraTime);
        LiveFlow.Next(LivePeriod.ExtraTimeSecondHalf, KnockoutDrawWithExtraTime).Should().Equal(LivePeriod.Penalties);
        LiveFlow.Next(LivePeriod.ExtraTimeSecondHalf, KnockoutWin).Should().Equal(LivePeriod.Ended);
        LiveFlow.Next(LivePeriod.SecondHalf, KnockoutWin).Should().Equal(LivePeriod.Ended);
    }

    [Theory]
    [InlineData(LivePeriod.FirstHalf, 0, 1, null)]
    [InlineData(LivePeriod.FirstHalf, 22, 23, null)]
    [InlineData(LivePeriod.FirstHalf, 46, 45, 2)]
    [InlineData(LivePeriod.SecondHalf, 10, 56, null)]
    [InlineData(LivePeriod.ExtraTimeFirstHalf, 3, 94, null)]
    public void Minute_counts_from_period_start(LivePeriod period, int elapsed, int minute, int? added)
    {
        var now = new DateTimeOffset(2026, 9, 12, 17, 0, 30, TimeSpan.Zero);
        LiveFlow.Minute(period, now.AddMinutes(-elapsed), 45, 15, now).Should().Be((minute, added));
    }

    [Fact]
    public void Status_follows_period()
    {
        LiveFlow.StatusFor(LivePeriod.FirstHalf).Should().Be(MatchStatus.Live);
        LiveFlow.StatusFor(LivePeriod.HalfTime).Should().Be(MatchStatus.HalfTime);
        LiveFlow.StatusFor(LivePeriod.Penalties).Should().Be(MatchStatus.Live);
        LiveFlow.StatusFor(LivePeriod.Ended).Should().Be(MatchStatus.Finished);
    }
}
