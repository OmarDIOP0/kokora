using Kokora.Domain.Enums;

namespace Kokora.Domain.Rules;

/// <summary>Règles de points d'une compétition (stockées en JSON).</summary>
public class ScoringRules
{
    public int PointsForWin { get; set; } = 3;
    public int PointsForDraw { get; set; } = 1;
    public int PointsForLoss { get; set; } = 0;
    /// <summary>Points de l'équipe déclarée forfait (peut être négatif).</summary>
    public int PointsForForfeitLoss { get; set; } = 0;
    /// <summary>Score attribué au vainqueur sur tapis vert.</summary>
    public int ForfeitGoalsFor { get; set; } = 3;
    public int ForfeitGoalsAgainst { get; set; } = 0;

    /// <summary>Critères de départage ordonnés, appliqués après les points.</summary>
    public List<TieBreaker> TieBreakers { get; set; } =
        [TieBreaker.GoalDifference, TieBreaker.GoalsFor, TieBreaker.HeadToHead, TieBreaker.FairPlay];

    /// <summary>Points de discipline pour le classement fair-play.</summary>
    public int FairPlayYellowPoints { get; set; } = 1;
    public int FairPlaySecondYellowPoints { get; set; } = 3;
    public int FairPlayRedPoints { get; set; } = 4;

    public static ScoringRules Default() => new();
}

/// <summary>Règles de suspension automatique (stockées en JSON).</summary>
public class SuspensionRules
{
    public bool Enabled { get; set; } = true;
    /// <summary>Nombre de cartons jaunes (cumulés) entraînant une suspension. 0 = désactivé.</summary>
    public int YellowCardsThreshold { get; set; } = 3;
    public int MatchesForYellowAccumulation { get; set; } = 1;
    public int MatchesForSecondYellow { get; set; } = 1;
    public int MatchesForDirectRed { get; set; } = 1;
    /// <summary>Les jaunes non purgés sont remis à zéro au début d'une nouvelle phase.</summary>
    public bool ResetYellowsEachPhase { get; set; } = false;

    public static SuspensionRules Default() => new();
}
