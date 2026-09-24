using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Kokora.Application.Abstractions;
using Kokora.Application.Engagement;
using Kokora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebPush;

namespace Kokora.Infrastructure.Push;

/// <summary>
/// Envoi des notifications Web Push (protocole standard, sans service tiers : Chrome, Firefox, Edge, Safari).
/// Clés VAPID : Push:PublicKey / Push:PrivateKey / Push:Subject ; à défaut, une paire est générée au premier
/// démarrage et conservée dans App_Data/vapid.json (à sauvegarder : la changer invalide les abonnements existants).
/// Les envois partent d'une file en mémoire, traitée en tâche de fond.
/// </summary>
public class WebPushService : BackgroundService, IPushService
{
    private readonly Channel<(PushMessage Message, int[] Ids)> _queue =
        Channel.CreateBounded<(PushMessage, int[])>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebPushService> _logger;
    private readonly VapidDetails? _vapid;
    private readonly WebPushClient _client = new();

    public string? PublicKey => _vapid?.PublicKey;

    public WebPushService(IConfiguration config, IHostEnvironment env, IServiceScopeFactory scopes, ILogger<WebPushService> logger,
        Storage.IBlobStore blobs)
    {
        _scopes = scopes;
        _logger = logger;
        var subject = config["Push:Subject"] ?? "mailto:contact@kokora.sn";
        var (pub, priv) = (config["Push:PublicKey"], config["Push:PrivateKey"]);
        if (string.IsNullOrWhiteSpace(pub) || string.IsNullOrWhiteSpace(priv))
        {
            try
            {
                (pub, priv) = DependencyInjection.IsDatabaseStorage(config)
                    ? LoadOrCreateKeysInDatabase(blobs)
                    : LoadOrCreateKeys(Path.Combine(config["Storage:DataPath"] is { Length: > 0 } data ? data : Path.Combine(env.ContentRootPath, "App_Data"), "vapid.json"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notifications push désactivées : impossible de créer les clés VAPID.");
                return;
            }
        }
        _vapid = new VapidDetails(subject, pub, priv);
    }

    private (string, string) LoadOrCreateKeys(string path)
    {
        if (File.Exists(path))
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
            return (saved["publicKey"], saved["privateKey"]);
        }
        var keys = VapidHelper.GenerateVapidKeys();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["publicKey"] = keys.PublicKey, ["privateKey"] = keys.PrivateKey
        }));
        _logger.LogInformation("Clés VAPID générées dans {Path} (à conserver et sauvegarder).", path);
        return (keys.PublicKey, keys.PrivateKey);
    }

    /// <summary>Clés rangées avec les fichiers en base (clé réservée, jamais servie publiquement).</summary>
    private (string, string) LoadOrCreateKeysInDatabase(Storage.IBlobStore blobs)
    {
        const string key = "_system/vapid.json";
        var saved = blobs.ReadAsync(key).GetAwaiter().GetResult();
        if (saved is { } file)
        {
            var keys = JsonSerializer.Deserialize<Dictionary<string, string>>(file.Data)!;
            return (keys["publicKey"], keys["privateKey"]);
        }
        var created = VapidHelper.GenerateVapidKeys();
        blobs.WriteAsync(key, JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["publicKey"] = created.PublicKey, ["privateKey"] = created.PrivateKey
        }), "application/json").GetAwaiter().GetResult();
        _logger.LogInformation("Clés VAPID générées et enregistrées en base.");
        return (created.PublicKey, created.PrivateKey);
    }

    public void Enqueue(PushMessage message, IReadOnlyCollection<int> subscriptionIds)
    {
        if (_vapid is null || subscriptionIds.Count == 0) return;
        _queue.Writer.TryWrite((message, subscriptionIds.ToArray()));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (message, ids) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await SendAsync(message, ids, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Échec d'un lot de notifications « {Title} »", message.Title);
            }
        }
    }

    private async Task SendAsync(PushMessage message, int[] ids, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subs = await db.PushSubscriptions.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        var payload = JsonSerializer.Serialize(new { title = message.Title, body = message.Body, url = message.Url, tag = message.Tag });
        var gone = new List<Kokora.Domain.Users.PushSubscription>();
        var options = new Dictionary<string, object> { ["vapidDetails"] = _vapid!, ["TTL"] = message.TtlSeconds };

        // Envois en parallèle limité : une finale peut concerner des milliers d'abonnés.
        await Parallel.ForEachAsync(subs, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (s, token) =>
        {
            try
            {
                await _client.SendNotificationAsync(new WebPush.PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, options, token);
                s.LastSuccessAt = DateTimeOffset.UtcNow;
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                lock (gone) gone.Add(s); // abonnement expiré ou révoqué par l'utilisateur
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Notification non remise à l'abonnement {Id}", s.Id);
            }
        });
        db.PushSubscriptions.RemoveRange(gone);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Chaque minute : notifications des infos importantes programmées dont l'heure est arrivée.</summary>
public class ScheduledNewsNotifier(IServiceScopeFactory scopes, ILogger<ScheduledNewsNotifier> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NotificationService>().NotifyDueArticlesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Vérification des infos programmées impossible.");
            }
        }
    }
}
