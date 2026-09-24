using Kokora.Domain.Common;

namespace Kokora.Domain.Clubs;

/// <summary>ASC (Association Sportive et Culturelle) de quartier.</summary>
public class Club : Entity, IAuditable, IDemoData
{
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? LogoPath { get; set; }
    public string PrimaryColor { get; set; } = "#0E6B3A";
    public string SecondaryColor { get; set; } = "#FFFFFF";
    public string? Neighborhood { get; set; }
    /// <summary>Zone de rattachement (ex. « 5A », « 5B »).</summary>
    public string? Zone { get; set; }
    public string? ManagerName { get; set; }
    public string? ContactPhone { get; set; }
    public int? FoundedYear { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDemo { get; set; }

    public List<SquadMember> Squad { get; set; } = [];

    public string Initials => ClubInitials.From(ShortName.Length > 0 ? ShortName : Name);
}

public static class ClubInitials
{
    private static readonly HashSet<string> Ignored =
        new(StringComparer.OrdinalIgnoreCase) { "ASC", "AS", "de", "du", "des", "la", "le", "les", "d" };

    public static string From(string name)
    {
        var words = name.Split([' ', '-', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Ignored.Contains(w)).ToList();
        if (words.Count == 0) words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 0) return "?";
        if (words.Count == 1) return words[0][..Math.Min(3, words[0].Length)].ToUpperInvariant();
        // Un nombre est gardé en entier (« Démo 12 » → « D12 », pas « D1 »), dans la limite de 3 caractères.
        var initials = string.Concat(words.Select(w => w.All(char.IsDigit) ? w : char.ToUpperInvariant(w[0]).ToString()));
        return initials[..Math.Min(3, initials.Length)];
    }
}
