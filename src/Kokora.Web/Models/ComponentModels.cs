using Kokora.Domain.Enums;

namespace Kokora.Web.Models;

/// <summary>Équipe telle qu'affichée dans une liste (écusson + nom).</summary>
public record TeamVm(int Id, string Name, string ShortName, string Slug, string Color, string? Color2 = null, string? LogoUrl = null);

/// <summary>Une ligne de match (listes, fiches équipe, widgets).</summary>
public record MatchRowVm
{
    public int Id { get; init; }
    public string Url { get; init; } = "#";
    public TeamVm? Home { get; init; }
    public TeamVm? Away { get; init; }
    public string? HomePlaceholder { get; init; }
    public string? AwayPlaceholder { get; init; }
    public DateTimeOffset? KickoffAt { get; init; }
    public MatchStatus Status { get; init; }
    public int? HomeScore { get; init; }
    public int? AwayScore { get; init; }
    public int? HomePenalties { get; init; }
    public int? AwayPenalties { get; init; }
    public int? Minute { get; init; }
    public string? Stadium { get; init; }

    public bool IsLive => Status is MatchStatus.Live or MatchStatus.HalfTime;
    public bool ShowScore => HomeScore.HasValue && AwayScore.HasValue &&
        Status is MatchStatus.Live or MatchStatus.HalfTime or MatchStatus.Finished or MatchStatus.Forfeit
            or MatchStatus.Abandoned or MatchStatus.UnderReview;

    /// <summary>+1 domicile gagne, -1 extérieur gagne, 0 nul/indéterminé (tirs au but inclus).</summary>
    public int Outcome
    {
        get
        {
            if (!ShowScore || IsLive) return 0;
            var diff = HomeScore!.Value - AwayScore!.Value;
            if (diff == 0 && HomePenalties.HasValue && AwayPenalties.HasValue) diff = HomePenalties.Value - AwayPenalties.Value;
            return Math.Sign(diff);
        }
    }
}

public record MatchGroupVm(string Title, string? Subtitle, string Color, string? Url, IReadOnlyList<MatchRowVm> Matches);

public record StandingRowVm
{
    public int Rank { get; init; }
    public required TeamVm Team { get; init; }
    public int Played { get; init; }
    public int Won { get; init; }
    public int Drawn { get; init; }
    public int Lost { get; init; }
    public int GoalsFor { get; init; }
    public int GoalsAgainst { get; init; }
    public int Points { get; init; }
    /// <summary>5 derniers résultats, du plus ancien au plus récent : 'V', 'N', 'D'.</summary>
    public IReadOnlyList<char> Form { get; init; } = [];
    /// <summary>'q' qualifié, 'p' barrage, 'e' éliminé, ou null.</summary>
    public char? Zone { get; init; }
    public int GoalDifference => GoalsFor - GoalsAgainst;
}

public record StandingsTableVm(string Title, IReadOnlyList<StandingRowVm> Rows, IReadOnlyList<(char Zone, string Label)> Legend, bool Compact = false);
