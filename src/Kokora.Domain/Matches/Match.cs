using Kokora.Domain.Clubs;
using Kokora.Domain.Common;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;

namespace Kokora.Domain.Matches;

public class Match : Entity, IAuditable, IDemoData
{
    public int PhaseId { get; set; }
    public Phase Phase { get; set; } = null!;
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
    public int? RoundId { get; set; }
    public Round? Round { get; set; }
    /// <summary>Position dans le tableau d'élimination (1 = premier match du tour).</summary>
    public int? BracketPosition { get; set; }
    /// <summary>Journée (phase de championnat).</summary>
    public int? Matchday { get; set; }

    /// <summary>Équipes : nulles tant qu'elles ne sont pas connues (ex. « Vainqueur QF1 »).</summary>
    public int? HomeClubId { get; set; }
    public Club? HomeClub { get; set; }
    public int? AwayClubId { get; set; }
    public Club? AwayClub { get; set; }
    public string? HomePlaceholder { get; set; }
    public string? AwayPlaceholder { get; set; }

    public DateTimeOffset? KickoffAt { get; set; }
    public int? StadiumId { get; set; }
    public Stadium? Stadium { get; set; }
    public int? RefereeId { get; set; }
    public Referee? Referee { get; set; }

    public MatchStatus Status { get; set; } = MatchStatus.Scheduled;

    /// <summary>Score à la fin du match (prolongations incluses, hors tirs au but).</summary>
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public int? HomeHalfTimeScore { get; set; }
    public int? AwayHalfTimeScore { get; set; }
    public bool WentToExtraTime { get; set; }
    public int? HomePenalties { get; set; }
    public int? AwayPenalties { get; set; }

    /// <summary>Équipe déclarée perdante par forfait (score administratif appliqué).</summary>
    public int? ForfeitingClubId { get; set; }

    // --- Direct ---
    public LivePeriod LivePeriod { get; set; } = LivePeriod.NotStarted;
    /// <summary>Instant de début de la période en cours, pour calculer la minute.</summary>
    public DateTimeOffset? PeriodStartedAt { get; set; }

    /// <summary>Match aller (pour un match retour).</summary>
    public int? FirstLegMatchId { get; set; }
    public Match? FirstLeg { get; set; }

    /// <summary>Homme du match désigné par l'organisation (le vote des supporters est à part).</summary>
    public int? ManOfTheMatchPlayerId { get; set; }
    public Kokora.Domain.Clubs.Player? ManOfTheMatch { get; set; }

    public bool IsFeatured { get; set; }
    public string? Notes { get; set; }
    public bool IsDemo { get; set; }

    public List<MatchEvent> Events { get; set; } = [];
    public List<LineupEntry> Lineups { get; set; } = [];

    /// <summary>Jeton de concurrence optimiste (xmin sous PostgreSQL).</summary>
    public uint Version { get; set; }

    public bool HasResult => HomeScore.HasValue && AwayScore.HasValue &&
        Status is MatchStatus.Finished or MatchStatus.Forfeit or MatchStatus.UnderReview;

    public bool IsLive => Status is MatchStatus.Live or MatchStatus.HalfTime;
}
