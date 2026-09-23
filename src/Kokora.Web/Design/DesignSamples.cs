using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Web.Models;

namespace Kokora.Web.Design;

/// <summary>Données FICTIVES utilisées uniquement par la page /design (aucune base requise).</summary>
public static class DesignSamples
{
    public static readonly TeamVm[] Teams =
    [
        new(1, "ASC Démo 1", "Démo 1", "demo-1", "#0E6B3A", "#FFFFFF"),
        new(2, "ASC Démo 2", "Démo 2", "demo-2", "#C62828", "#FFD54F"),
        new(3, "ASC Démo 3", "Démo 3", "demo-3", "#1A237E", "#FFFFFF"),
        new(4, "ASC Démo 4", "Démo 4", "demo-4", "#F9A825", "#1B1B1B"),
        new(5, "ASC Démo 5", "Démo 5", "demo-5", "#6A1B9A", "#FFFFFF"),
        new(6, "ASC Démo 6", "Démo 6", "demo-6", "#00838F", "#FFFFFF"),
        new(7, "ASC Démo 7", "Démo 7", "demo-7", "#FFFFFF", "#111111"),
        new(8, "ASC Démo 8 Longue Dénomination", "Démo 8", "demo-8", "#4E342E", "#FFCC80"),
    ];

    public const string ColorZoneA = "#0E6B3A";
    public const string ColorZoneB = "#B5471B";
    public const string ColorGrandes = "#14213D";
    public const string ColorCoupe = "#9A7A2E";

    public static IReadOnlyList<(string Name, string Color)> Competitions =>
    [
        ("Zonale 5A", ColorZoneA), ("Zonale 5B", ColorZoneB), ("4 Grandes", ColorGrandes), ("Coupe du Maire", ColorCoupe)
    ];

    private static DateTimeOffset At(int dayOffset, int hour, int minute = 0)
    {
        var start = KokoraTime.StartOfDayUtc(KokoraTime.Today.AddDays(dayOffset));
        return start.AddHours(hour).AddMinutes(minute);
    }

    public static MatchRowVm Live => new()
    {
        Id = 1, Home = Teams[0], Away = Teams[1], Status = MatchStatus.Live, HomeScore = 2, AwayScore = 1,
        Minute = 67, KickoffAt = At(0, 16, 30), Stadium = "Stade Démo"
    };

    public static IReadOnlyList<MatchGroupVm> Today =>
    [
        new("Zonale 5A", "Poule A · 4e journée", ColorZoneA, "#",
        [
            Live,
            new() { Id = 2, Home = Teams[2], Away = Teams[3], Status = MatchStatus.HalfTime, HomeScore = 0, AwayScore = 0, KickoffAt = At(0, 16, 0) },
            new() { Id = 3, Home = Teams[4], Away = Teams[5], Status = MatchStatus.Scheduled, KickoffAt = At(0, 18, 30) },
        ]),
        new("Zonale 5B", "Poule B · 4e journée", ColorZoneB, "#",
        [
            new() { Id = 4, Home = Teams[6], Away = Teams[7], Status = MatchStatus.Finished, HomeScore = 3, AwayScore = 1, KickoffAt = At(0, 10, 0) },
            new() { Id = 5, Home = Teams[1], Away = Teams[5], Status = MatchStatus.Postponed, KickoffAt = At(0, 16, 30) },
            new() { Id = 6, Home = Teams[3], Away = Teams[0], Status = MatchStatus.Forfeit, HomeScore = 0, AwayScore = 3, KickoffAt = At(0, 16, 30) },
        ]),
        new("Coupe du Maire", "Demi-finales", ColorCoupe, "#",
        [
            new() { Id = 7, Home = Teams[2], Away = Teams[4], Status = MatchStatus.Finished, HomeScore = 1, AwayScore = 1, HomePenalties = 4, AwayPenalties = 3, KickoffAt = At(0, 17, 0) },
            new() { Id = 8, Home = Teams[5], Away = Teams[6], Status = MatchStatus.UnderReview, HomeScore = 2, AwayScore = 2, KickoffAt = At(0, 17, 0) },
            new() { Id = 9, HomePlaceholder = "Vainqueur DF1", AwayPlaceholder = "Vainqueur DF2", Status = MatchStatus.Scheduled, KickoffAt = At(6, 17, 0) },
        ]),
    ];

    public static StandingsTableVm Standings(bool compact = false) => new(
        "Zonale 5A · Poule A",
        [
            Row(1, 0, 4, 3, 1, 0, 9, 3, 10, "VVNV", 'q'),
            Row(2, 2, 4, 3, 0, 1, 7, 4, 9, "VDVV", 'q'),
            Row(3, 4, 4, 2, 1, 1, 6, 5, 7, "NVVD", 'p'),
            Row(4, 1, 4, 1, 2, 1, 5, 5, 5, "DNVN", null),
            Row(5, 6, 4, 1, 0, 3, 3, 8, 3, "DVDD", null),
            Row(6, 7, 4, 0, 0, 4, 2, 7, -3, "DDDD", 'e'),
        ],
        [('q', "Quart de finale"), ('p', "Barrage"), ('e', "Éliminé")],
        compact);

    private static StandingRowVm Row(int rank, int team, int p, int w, int d, int l, int gf, int ga, int pts, string form, char? zone) =>
        new()
        {
            Rank = rank, Team = Teams[team], Played = p, Won = w, Drawn = d, Lost = l,
            GoalsFor = gf, GoalsAgainst = ga, Points = pts, Form = form.ToCharArray(), Zone = zone
        };

    public static IReadOnlyList<DateOnly> Days()
    {
        var today = KokoraTime.Today;
        return Enumerable.Range(-5, 12).Select(i => today.AddDays(i)).ToList();
    }

    public static DateTimeOffset NextBigMatch => At(6, 17, 0);
}
