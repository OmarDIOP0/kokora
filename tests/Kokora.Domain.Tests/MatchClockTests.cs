using FluentAssertions;
using Kokora.Domain.Enums;
using Kokora.Domain.Rules;

namespace Kokora.Domain.Tests;

public class MatchClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 16, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(LivePeriod.FirstHalf, 0, "1")]
    [InlineData(LivePeriod.FirstHalf, 22.5, "23")]
    [InlineData(LivePeriod.FirstHalf, 46.2, "45+2")]
    [InlineData(LivePeriod.SecondHalf, 0, "46")]
    [InlineData(LivePeriod.SecondHalf, 44.9, "90")]
    [InlineData(LivePeriod.SecondHalf, 47, "90+3")]
    [InlineData(LivePeriod.ExtraTimeFirstHalf, 5, "96")]
    [InlineData(LivePeriod.ExtraTimeSecondHalf, 16, "120+2")]
    public void Running_periods(LivePeriod period, double minutesElapsed, string expected) =>
        MatchClock.Label(period, Start, 45, 15, Start.AddMinutes(minutesElapsed)).Should().Be(expected);

    [Theory]
    [InlineData(LivePeriod.HalfTime, "MT")]
    [InlineData(LivePeriod.Penalties, "TAB")]
    [InlineData(LivePeriod.Ended, null)]
    public void Breaks(LivePeriod period, string? expected) =>
        MatchClock.Label(period, Start, 45, 15, Start.AddMinutes(3)).Should().Be(expected);

    [Fact]
    public void Shorter_halves_are_supported() =>
        MatchClock.Label(LivePeriod.SecondHalf, Start, 40, 10, Start.AddMinutes(10)).Should().Be("51");
}
