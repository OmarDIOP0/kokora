using Kokora.Domain.Clubs;
using Kokora.Domain.Common;
using Kokora.Domain.Enums;

namespace Kokora.Domain.Competitions;

/// <summary>Poule d'une phase de championnat.</summary>
public class Group : Entity, IAuditable, IDemoData
{
    public int PhaseId { get; set; }
    public Phase Phase { get; set; } = null!;

    public string Name { get; set; } = "";
    public int Order { get; set; }

    /// <summary>Zones colorées du classement (places qualificatives, barrages…).</summary>
    public List<StandingZone> Zones { get; set; } = [];

    public bool IsDemo { get; set; }

    public List<GroupTeam> Teams { get; set; } = [];
    public List<PointAdjustment> PointAdjustments { get; set; } = [];
}

public class StandingZone
{
    public int FromRank { get; set; }
    public int ToRank { get; set; }
    public StandingZoneKind Kind { get; set; } = StandingZoneKind.Qualified;
    public string Label { get; set; } = "";
}

public class GroupTeam
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public int ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public int Seed { get; set; }
}

/// <summary>Pénalité (ou bonus) de points décidée par la commission.</summary>
public class PointAdjustment : Entity, IAuditable, IDemoData
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;
    public int ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public int Points { get; set; }
    public string Reason { get; set; } = "";
    public DateOnly DecidedOn { get; set; }
    public bool IsDemo { get; set; }
}
