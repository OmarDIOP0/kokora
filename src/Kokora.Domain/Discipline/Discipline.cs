using Kokora.Domain.Clubs;
using Kokora.Domain.Common;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;

namespace Kokora.Domain.Discipline;

/// <summary>
/// Suspension manuelle décidée par la commission. Les suspensions automatiques
/// (cumul de cartons) sont recalculées à la volée à partir des événements de match.
/// </summary>
public class Suspension : Entity, IAuditable, IDemoData
{
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int CompetitionId { get; set; }
    public Competition Competition { get; set; } = null!;
    public int? ClubId { get; set; }
    public Club? Club { get; set; }
    public int Matches { get; set; } = 1;
    public string Reason { get; set; } = "";
    public SuspensionSource Source { get; set; } = SuspensionSource.Manual;
    /// <summary>Match à l'origine de la sanction.</summary>
    public int? TriggerMatchId { get; set; }
    public Match? TriggerMatch { get; set; }
    public DateOnly DecidedOn { get; set; }
    public bool IsDemo { get; set; }
}

/// <summary>Réserve (réclamation) déposée auprès de la commission.</summary>
public class Protest : Entity, IAuditable, IDemoData
{
    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public string Subject { get; set; } = "";
    public ProtestStatus Status { get; set; } = ProtestStatus.Filed;
    public string? Decision { get; set; }
    public DateOnly FiledOn { get; set; }
    public DateOnly? DecidedOn { get; set; }
    public bool IsDemo { get; set; }
}
