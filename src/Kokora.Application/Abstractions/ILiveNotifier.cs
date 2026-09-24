using Kokora.Domain.Enums;

namespace Kokora.Application.Abstractions;

/// <summary>État diffusé en temps réel après chaque changement d'un match (score, période, événement).</summary>
public record LiveUpdate(
    int MatchId,
    MatchStatus Status,
    LivePeriod Period,
    int? HomeScore,
    int? AwayScore,
    int? HomePenalties,
    int? AwayPenalties,
    DateTimeOffset? PeriodStartedAt,
    int HalfMinutes,
    int ExtraHalfMinutes,
    /// <summary>Résumé lisible du dernier changement (« But ! ASC Démo 1, 23' »), pour les notifications.</summary>
    string? Text);

/// <summary>Diffuse les changements de match aux navigateurs connectés (implémenté avec SignalR dans la couche Web).</summary>
public interface ILiveNotifier
{
    Task MatchUpdatedAsync(LiveUpdate update, CancellationToken ct = default);
}
