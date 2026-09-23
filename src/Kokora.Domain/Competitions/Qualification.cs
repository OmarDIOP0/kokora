using Kokora.Domain.Clubs;
using Kokora.Domain.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;

namespace Kokora.Domain.Competitions;

/// <summary>
/// Règle de passage d'une équipe vers une phase suivante (même compétition ou autre),
/// ex. « 1er poule A → demi-finale 1, domicile » ou « vainqueur demi 1 → 4 Grandes ».
/// </summary>
public class Qualification : Entity, IAuditable, IDemoData
{
    public QualificationSource Source { get; set; }
    public int? SourceGroupId { get; set; }
    public Group? SourceGroup { get; set; }
    public int? SourceRank { get; set; }
    public int? SourceMatchId { get; set; }
    public Match? SourceMatch { get; set; }

    public int TargetPhaseId { get; set; }
    public Phase TargetPhase { get; set; } = null!;
    public int? TargetGroupId { get; set; }
    public Group? TargetGroup { get; set; }
    public int? TargetMatchId { get; set; }
    public Match? TargetMatch { get; set; }
    public MatchSlot? TargetSlot { get; set; }

    /// <summary>Équipe résolue lors de la génération (ou saisie manuellement).</summary>
    public int? ClubId { get; set; }
    public Club? Club { get; set; }
    public bool IsManualOverride { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool IsDemo { get; set; }
}
