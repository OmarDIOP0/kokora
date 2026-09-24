using Kokora.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Kokora.Web.Live;

/// <summary>
/// Canal temps réel public (lecture seule) : le serveur pousse l'état des matchs, les navigateurs ne peuvent rien envoyer.
/// Le volume reste faible (quelques matchs en même temps) : chaque mise à jour est envoyée à tous les connectés,
/// chaque page ne retient que les matchs qu'elle affiche.
/// </summary>
public class LiveHub : Hub
{
    public const string Path = "/direct/hub";
    public const string MatchMessage = "match";
}

public class SignalRLiveNotifier(IHubContext<LiveHub> hub, ILogger<SignalRLiveNotifier> logger) : ILiveNotifier
{
    public async Task MatchUpdatedAsync(LiveUpdate update, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.All.SendAsync(LiveHub.MatchMessage, update, ct);
        }
        catch (Exception ex)
        {
            // La diffusion ne doit jamais faire échouer l'enregistrement : les pages se rafraîchissent aussi périodiquement.
            logger.LogWarning(ex, "Diffusion temps réel impossible pour le match {MatchId}", update.MatchId);
        }
    }
}
