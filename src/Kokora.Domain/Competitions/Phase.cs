using Kokora.Domain.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;

namespace Kokora.Domain.Competitions;

public class Phase : Entity, IAuditable, IDemoData
{
    public int CompetitionId { get; set; }
    public Competition Competition { get; set; } = null!;

    public string Name { get; set; } = "";
    public int Order { get; set; }
    public PhaseType Type { get; set; } = PhaseType.League;
    public Legs Legs { get; set; } = Legs.Single;

    /// <summary>Élimination : prolongations en cas d'égalité.</summary>
    public bool HasExtraTime { get; set; }
    /// <summary>Élimination : tirs au but en cas d'égalité.</summary>
    public bool HasPenalties { get; set; } = true;

    public bool IsDemo { get; set; }

    public List<Group> Groups { get; set; } = [];
    public List<Round> Rounds { get; set; } = [];
    public List<Match> Matches { get; set; } = [];
}
