namespace Kokora.Domain.Enums;

public enum PhaseType
{
    League = 1,     // Championnat (poules avec classement)
    Knockout = 2    // Élimination directe
}

public enum Legs
{
    Single = 1,     // Aller simple
    Double = 2      // Aller-retour
}

public enum RoundKind
{
    RoundOf32 = 1,
    RoundOf16 = 2,
    QuarterFinal = 3,
    SemiFinal = 4,
    ThirdPlace = 5,
    Final = 6,
    Other = 9
}

public enum MatchStatus
{
    Scheduled = 1,      // Programmé
    Postponed = 2,      // Reporté
    Live = 3,           // En direct
    HalfTime = 4,       // Mi-temps
    Finished = 5,       // Terminé
    Forfeit = 6,        // Forfait
    Abandoned = 7,      // Arrêté
    Replay = 8,         // À rejouer
    UnderReview = 9     // Sous réserve (litige à la commission)
}

public enum LivePeriod
{
    NotStarted = 0,
    FirstHalf = 1,
    HalfTime = 2,
    SecondHalf = 3,
    BreakBeforeExtraTime = 4,
    ExtraTimeFirstHalf = 5,
    ExtraTimeHalfTime = 6,
    ExtraTimeSecondHalf = 7,
    Penalties = 8,
    Ended = 9
}

public enum MatchEventType
{
    Goal = 1,
    PenaltyGoal = 2,
    OwnGoal = 3,
    MissedPenalty = 4,
    YellowCard = 10,
    SecondYellow = 11,  // 2e jaune = expulsion
    RedCard = 12,
    Substitution = 20,
    PeriodStart = 30,
    PeriodEnd = 31,
    ShootoutKick = 40,  // tir au but (IsScored dans Note)
    Info = 50
}

public enum PlayerPosition
{
    Unknown = 0,
    Goalkeeper = 1,
    Defender = 2,
    Midfielder = 3,
    Forward = 4
}

public enum TieBreaker
{
    GoalDifference = 1,
    GoalsFor = 2,
    HeadToHead = 3,     // points, diff. puis buts dans les confrontations directes
    FairPlay = 4,       // moins de points de discipline
    Wins = 5,
    Draw = 99           // tirage au sort / décision manuelle
}

public enum StandingZoneKind
{
    Qualified = 1,
    Playoff = 2,
    Eliminated = 3
}

public enum QualificationSource
{
    GroupRank = 1,      // rang X de la poule
    MatchWinner = 2,
    MatchLoser = 3,
    Manual = 4
}

public enum MatchSlot
{
    Home = 1,
    Away = 2
}

public enum SuspensionSource
{
    Automatic = 1,
    Manual = 2
}

public enum ProtestStatus
{
    Filed = 1,          // Déposée
    Accepted = 2,       // Fondée
    Rejected = 3,       // Rejetée
    Withdrawn = 4
}

public enum ArticleStatus
{
    Draft = 1,
    Scheduled = 2,
    Published = 3,
    Archived = 4
}

public enum CommentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}
