using Kokora.Domain.Enums;

namespace Kokora.Domain.Rules;

/// <summary>Minute affichée pendant un match en direct (« 23 », « 45+2 », « MT »).</summary>
public static class MatchClock
{
    public static string? Label(LivePeriod period, DateTimeOffset? periodStartedAt, int halfMinutes, int extraHalfMinutes, DateTimeOffset now)
    {
        (int Base, int Length)? running = period switch
        {
            LivePeriod.FirstHalf => (0, halfMinutes),
            LivePeriod.SecondHalf => (halfMinutes, halfMinutes),
            LivePeriod.ExtraTimeFirstHalf => (2 * halfMinutes, extraHalfMinutes),
            LivePeriod.ExtraTimeSecondHalf => (2 * halfMinutes + extraHalfMinutes, extraHalfMinutes),
            _ => null
        };
        if (running is { } r)
        {
            if (periodStartedAt is null) return (r.Base + 1).ToString();
            var elapsed = (int)Math.Floor((now - periodStartedAt.Value).TotalMinutes) + 1;
            elapsed = Math.Max(1, elapsed);
            return elapsed > r.Length ? $"{r.Base + r.Length}+{elapsed - r.Length}" : (r.Base + elapsed).ToString();
        }
        return period switch
        {
            LivePeriod.HalfTime or LivePeriod.ExtraTimeHalfTime => "MT",
            LivePeriod.BreakBeforeExtraTime => "Pause",
            LivePeriod.Penalties => "TAB",
            _ => null
        };
    }
}
