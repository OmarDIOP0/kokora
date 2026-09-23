using Kokora.Domain.Clubs;
using Kokora.Domain.Common;
using Kokora.Domain.Matches;

namespace Kokora.Domain.Users;

public class FavoriteClub
{
    public string UserId { get; set; } = "";
    public int ClubId { get; set; }
    public Club Club { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PushSubscription : Entity
{
    /// <summary>Nul pour un visiteur anonyme (notifications générales seulement).</summary>
    public string? UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string? UserAgent { get; set; }
    public bool NotifyKickoff { get; set; } = true;
    public bool NotifyGoals { get; set; } = true;
    public bool NotifyFullTime { get; set; } = true;
    public bool NotifyNews { get; set; } = true;
    public DateTimeOffset? LastSuccessAt { get; set; }
}

public class Prediction : Entity
{
    public string UserId { get; set; } = "";
    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    /// <summary>Points attribués une fois le match terminé.</summary>
    public int? Points { get; set; }
}

public class ManOfTheMatchVote : Entity
{
    public string UserId { get; set; } = "";
    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    /// <summary>Added, Modified, Deleted ou action métier (ex. « Match.Goal »).</summary>
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    /// <summary>Valeurs avant/après en JSON.</summary>
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>Compteur de visites agrégé par jour et par section (pas de suivi individuel).</summary>
public class DailyVisit
{
    public DateOnly Day { get; set; }
    public string Section { get; set; } = "";
    public int Count { get; set; }
}
