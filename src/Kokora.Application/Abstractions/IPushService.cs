namespace Kokora.Application.Abstractions;

/// <summary>Contenu d'une notification push (affichée par le service worker).</summary>
public record PushMessage(string Title, string Body, string Url, string Tag, int TtlSeconds = 3600);

/// <summary>Envoi des notifications Web Push, en arrière-plan (la saisie en direct n'attend jamais les envois).</summary>
public interface IPushService
{
    /// <summary>Clé publique VAPID transmise au navigateur lors de l'abonnement.</summary>
    string? PublicKey { get; }

    /// <summary>Met les envois en file d'attente (un par abonnement).</summary>
    void Enqueue(PushMessage message, IReadOnlyCollection<int> subscriptionIds);
}
