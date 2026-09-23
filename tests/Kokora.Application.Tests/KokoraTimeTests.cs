using FluentAssertions;
using Kokora.Application.Common;

namespace Kokora.Application.Tests;

public class KokoraTimeTests
{
    [Fact]
    public void Long_formats_in_french_with_dakar_time()
    {
        // Dakar = GMT, sans heure d'été.
        var kickoff = new DateTimeOffset(2026, 9, 12, 16, 30, 0, TimeSpan.Zero);
        KokoraTime.Long(kickoff).Should().Be("Samedi 12 septembre, 16h30");
    }

    [Fact]
    public void Hour_omits_zero_minutes()
    {
        KokoraTime.Hour(new DateTimeOffset(2026, 9, 12, 17, 0, 0, TimeSpan.Zero)).Should().Be("17h");
        KokoraTime.Hour(new DateTimeOffset(2026, 9, 12, 9, 5, 0, TimeSpan.Zero)).Should().Be("9h05");
    }

    [Fact]
    public void Utc_offsets_are_converted_to_local_time()
    {
        var parisEvening = new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.FromHours(2));
        KokoraTime.Hour(parisEvening).Should().Be("18h");
    }

    [Fact]
    public void DayRelative_uses_today_and_tomorrow()
    {
        KokoraTime.DayRelative(KokoraTime.Today).Should().Be("Aujourd'hui");
        KokoraTime.DayRelative(KokoraTime.Today.AddDays(1)).Should().Be("Demain");
        KokoraTime.DayRelative(KokoraTime.Today.AddDays(-1)).Should().Be("Hier");
    }
}
