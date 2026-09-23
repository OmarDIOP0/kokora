using Kokora.Domain.Common;
using Kokora.Domain.Rules;

namespace Kokora.Domain.Competitions;

/// <summary>Zonale 5A, Zonale 5B, 4 Grandes, Coupe du Maire… ou toute autre compétition d'une saison.</summary>
public class Competition : Entity, IAuditable, IDemoData
{
    public int SeasonId { get; set; }
    public Season Season { get; set; } = null!;

    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Slug { get; set; } = "";
    public int Order { get; set; }
    /// <summary>Couleur d'accent (hex) pour repérer la compétition dans les listes.</summary>
    public string Color { get; set; } = "#0E6B3A";
    public string? Description { get; set; }

    /// <summary>Durée réglementaire d'une mi-temps, en minutes.</summary>
    public int HalfDurationMinutes { get; set; } = 45;
    public int ExtraTimeHalfDurationMinutes { get; set; } = 15;

    public ScoringRules Scoring { get; set; } = ScoringRules.Default();
    public SuspensionRules Suspensions { get; set; } = SuspensionRules.Default();

    public bool IsPublished { get; set; } = true;
    public bool IsDemo { get; set; }

    public List<Phase> Phases { get; set; } = [];
}
