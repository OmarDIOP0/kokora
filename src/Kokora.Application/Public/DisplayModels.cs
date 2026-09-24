using Kokora.Domain.Enums;

namespace Kokora.Application.Public;

/// <summary>Équipe telle qu'affichée dans une liste (écusson + nom).</summary>
public record TeamVm(int Id, string Name, string ShortName, string Slug, string Color, string? Color2 = null, string? LogoUrl = null)
{
    public string Url => $"/equipes/{Slug}";
}

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
    public bool WentToExtraTime { get; init; }
    /// <summary>Minute affichée en direct (« 23 », « 45+2 »).</summary>
    public string? Minute { get; init; }
    public string? Stadium { get; init; }
    public int CompetitionId { get; init; }
    public string? Competition { get; init; }
    public string? CompetitionColor { get; init; }
    /// <summary>« Poule A · J3 » ou « Demi-finales ».</summary>
    public string? Stage { get; init; }
    public bool IsFeatured { get; init; }
    // Chronomètre calculé dans le navigateur.
    public LivePeriod Period { get; init; }
    public DateTimeOffset? PeriodStartedAt { get; init; }
    public int HalfMinutes { get; init; } = 45;
    public int ExtraHalfMinutes { get; init; } = 15;

    /// <summary>Match à suivre en temps réel : en cours, ou coup d'envoi proche (la page se connecte au direct).</summary>
    public bool Watch => IsLive || (Status == MatchStatus.Scheduled && KickoffAt is { } k
        && k < DateTimeOffset.UtcNow.AddHours(12) && k > DateTimeOffset.UtcNow.AddHours(-6));

    public bool IsLive => Status is MatchStatus.Live or MatchStatus.HalfTime;
    public bool ShowScore => HomeScore.HasValue && AwayScore.HasValue &&
        Status is MatchStatus.Live or MatchStatus.HalfTime or MatchStatus.Finished or MatchStatus.Forfeit
            or MatchStatus.Abandoned or MatchStatus.UnderReview;

    public bool Involves(IReadOnlySet<int> clubIds) =>
        clubIds.Count > 0 && ((Home is not null && clubIds.Contains(Home.Id)) || (Away is not null && clubIds.Contains(Away.Id)));

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
    public int Adjustment { get; init; }
    /// <summary>5 derniers résultats, du plus ancien au plus récent : 'V', 'N', 'D'.</summary>
    public IReadOnlyList<char> Form { get; init; } = [];
    /// <summary>'q' qualifié, 'p' barrage, 'e' éliminé, ou null.</summary>
    public char? Zone { get; init; }
    public int GoalDifference => GoalsFor - GoalsAgainst;
}

public record StandingsTableVm(string Title, IReadOnlyList<StandingRowVm> Rows, IReadOnlyList<(char Zone, string Label)> Legend, bool Compact = false)
{
    public IReadOnlySet<int> Favorites { get; init; } = new HashSet<int>();
}

public record GroupStandingsVm(int GroupId, string Name, StandingsTableVm Table, int PlayedMatches, int TotalMatches);

public record PhaseStandingsVm(int PhaseId, string Name, PhaseType Type, IReadOnlyList<GroupStandingsVm> Groups, BracketVm? Bracket);

public record BracketRoundVm(string Name, RoundKind Kind, IReadOnlyList<MatchRowVm> Matches);

public record BracketVm(IReadOnlyList<BracketRoundVm> Rounds)
{
    /// <summary>Rounds principaux (le match pour la 3e place est affiché à part).</summary>
    public IEnumerable<BracketRoundVm> MainRounds => Rounds.Where(r => r.Kind != RoundKind.ThirdPlace);
    public BracketRoundVm? ThirdPlace => Rounds.FirstOrDefault(r => r.Kind == RoundKind.ThirdPlace);
}

public record CompetitionVm(int Id, string Name, string ShortName, string Slug, string Color);

public record SeasonVm(int Id, int Year, string Name);

public record TimelineEventVm(int Id, MatchEventType Type, string Minute, bool IsHome, string? Player, string? PlayerSlug,
    string? Assist, string? PlayerOut, bool? IsScored, LivePeriod Period = LivePeriod.FirstHalf);

public record LineupPlayerVm(string Name, string? Slug, int? Number, bool IsCaptain, bool IsStarter, PlayerPosition Position);

public record MatchDetailVm
{
    public required MatchRowVm Match { get; init; }
    public required CompetitionVm Competition { get; init; }
    public string PhaseName { get; init; } = "";
    public string? Referee { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<TimelineEventVm> Events { get; init; } = [];
    public IReadOnlyList<LineupPlayerVm> HomeLineup { get; init; } = [];
    public IReadOnlyList<LineupPlayerVm> AwayLineup { get; init; } = [];
    public IReadOnlyList<MatchRowVm> HeadToHead { get; init; } = [];
    public int HomeYellow { get; init; }
    public int AwayYellow { get; init; }
    public int HomeRed { get; init; }
    public int AwayRed { get; init; }
    public int? HomeHalfTime { get; init; }
    public int? AwayHalfTime { get; init; }
    public (int HomeWins, int Draws, int AwayWins) HeadToHeadSummary { get; init; }
}

public record DayInfo(DateOnly Day, int Matches, bool HasLive);
