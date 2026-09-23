using Kokora.Domain.Clubs;
using Kokora.Domain.Common;
using Kokora.Domain.Enums;

namespace Kokora.Domain.Matches;

public class MatchEvent : Entity, IAuditable, IDemoData
{
    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public MatchEventType Type { get; set; }
    public LivePeriod Period { get; set; }
    public int Minute { get; set; }
    /// <summary>Temps additionnel (ex. 45+2 → Minute 45, AddedTime 2).</summary>
    public int? AddedTime { get; set; }

    public int? ClubId { get; set; }
    public Club? Club { get; set; }
    /// <summary>Buteur, joueur averti, joueur entrant.</summary>
    public int? PlayerId { get; set; }
    public Player? Player { get; set; }
    /// <summary>Passeur décisif.</summary>
    public int? AssistPlayerId { get; set; }
    public Player? AssistPlayer { get; set; }
    /// <summary>Joueur sortant (remplacement).</summary>
    public int? PlayerOutId { get; set; }
    public Player? PlayerOut { get; set; }

    /// <summary>Tir au but réussi (événement ShootoutKick).</summary>
    public bool? IsScored { get; set; }
    public string? Note { get; set; }

    public string? CreatedByUserId { get; set; }
    /// <summary>Action annulée (« annuler la dernière action ») : conservée pour l'audit.</summary>
    public bool IsCancelled { get; set; }
    public bool IsDemo { get; set; }

    public string MinuteLabel => AddedTime is > 0 ? $"{Minute}+{AddedTime}'" : $"{Minute}'";
}

public class LineupEntry : Entity, IDemoData
{
    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int ClubId { get; set; }
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public bool IsStarter { get; set; } = true;
    public int? ShirtNumber { get; set; }
    public bool IsCaptain { get; set; }
    public bool IsDemo { get; set; }
}
