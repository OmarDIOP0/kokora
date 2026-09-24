using Kokora.Domain.Enums;
using Kokora.Domain.Rules;

namespace Kokora.Application.Public;

public record PlayerVm(int Id, string Name, string Slug, string? PhotoUrl, PlayerPosition Position, int? Number = null)
{
    public string Url => $"/joueurs/{Slug}";
}

public record PlayerStatRow(PlayerVm Player, TeamVm? Team, int Goals, int Penalties, int Assists)
{
    public int Contributions => Goals + Assists;
}

public record TeamStatRow(TeamVm Team, int Played, int GoalsFor, int GoalsAgainst, int CleanSheets,
    int Yellow, int SecondYellow, int Red, int DisciplinePoints);

public record SuspensionRowVm(PlayerVm Player, TeamVm Team, string Competition, SuspensionReason Reason, string? Detail,
    int Matches, int Remaining, DateTimeOffset TriggeredAt)
{
    public string ReasonLabel => Reason switch
    {
        SuspensionReason.YellowAccumulation => Detail ?? "Cumul de cartons jaunes",
        SuspensionReason.SecondYellow => "Deux cartons jaunes (exclusion)",
        SuspensionReason.DirectRed => "Carton rouge direct",
        _ => Detail is null ? "Décision de la commission" : $"Commission : {Detail}"
    };
}

public record ThreatenedVm(PlayerVm Player, TeamVm Team, string Competition, int Yellows, int Threshold);

public record StatsPageData(
    IReadOnlyList<PlayerStatRow> Scorers,
    IReadOnlyList<PlayerStatRow> Assists,
    IReadOnlyList<PlayerStatRow> Contributions,
    IReadOnlyList<TeamStatRow> Teams,
    IReadOnlyList<SuspensionRowVm> Suspended,
    IReadOnlyList<ThreatenedVm> Threatened,
    int MatchesPlayed, int Goals);

public record PlayerMatchLine(MatchRowVm Match, int Goals, int Assists, bool Yellow, bool Red);

public record PlayerPageData(
    PlayerVm Player, string? Nickname, string FullName, TeamVm? Team, DateOnly? BirthDate, string? SeasonName,
    int Appearances, int Goals, int Penalties, int Assists, int Yellow, int Red,
    IReadOnlyList<(string Competition, int Goals, int Assists, int Yellow, int Red)> ByCompetition,
    IReadOnlyList<PlayerMatchLine> Matches,
    IReadOnlyList<SuspensionRowVm> Suspensions);

public record TeamPageData(
    TeamVm Team, string? Neighborhood, string? Zone, int? FoundedYear, string? SeasonName,
    IReadOnlyList<PlayerVm> Squad,
    IReadOnlyList<MatchRowVm> Upcoming,
    IReadOnlyList<MatchRowVm> Results,
    IReadOnlyList<(string Title, string Url, StandingsTableVm Table)> Standings,
    IReadOnlyList<PlayerStatRow> Scorers,
    TeamStatRow? Totals,
    IReadOnlyList<SuspensionRowVm> Suspended,
    IReadOnlyList<char> Form,
    IReadOnlyList<ArticleCardVm> News);

public record SearchResults(IReadOnlyList<TeamVm> Teams, IReadOnlyList<(PlayerVm Player, TeamVm? Team)> Players);
