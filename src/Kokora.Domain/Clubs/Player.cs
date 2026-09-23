using Kokora.Domain.Common;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;

namespace Kokora.Domain.Clubs;

public class Player : Entity, IAuditable, IDemoData
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    /// <summary>Surnom connu au quartier, affiché en priorité s'il existe.</summary>
    public string? Nickname { get; set; }
    public string Slug { get; set; } = "";
    public string? PhotoPath { get; set; }
    public PlayerPosition Position { get; set; }
    public DateOnly? BirthDate { get; set; }
    public bool IsDemo { get; set; }

    public List<SquadMember> Memberships { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? FullName : Nickname!;
}

/// <summary>Rattachement d'un joueur à une ASC pour une saison.</summary>
public class SquadMember : Entity, IAuditable, IDemoData
{
    public int SeasonId { get; set; }
    public Season Season { get; set; } = null!;
    public int ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int? ShirtNumber { get; set; }
    public string? LicenseNumber { get; set; }
    public bool IsCaptain { get; set; }
    public bool IsDemo { get; set; }
}
