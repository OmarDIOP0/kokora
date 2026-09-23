using Kokora.Domain.Common;
using Kokora.Domain.Enums;

namespace Kokora.Domain.Competitions;

/// <summary>Tour d'une phase à élimination (quarts, demies, finale…).</summary>
public class Round : Entity, IAuditable, IDemoData
{
    public int PhaseId { get; set; }
    public Phase Phase { get; set; } = null!;
    public string Name { get; set; } = "";
    public RoundKind Kind { get; set; }
    public int Order { get; set; }
    public bool IsDemo { get; set; }
}
