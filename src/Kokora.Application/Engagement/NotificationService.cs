using Kokora.Application.Abstractions;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Engagement;

public class SubscriptionInput
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public List<int> ClubIds { get; set; } = [];
    public bool NotifyKickoff { get; set; } = true;
    public bool NotifyGoals { get; set; } = true;
    public bool NotifyFullTime { get; set; } = true;
    public bool NotifyNews { get; set; } = true;
}

public record SubscriptionPrefs(bool NotifyKickoff, bool NotifyGoals, bool NotifyFullTime, bool NotifyNews, IReadOnlyList<int> ClubIds);

public enum MatchPushKind { Kickoff, Goal, FullTime }

public record PushAudience(int All, int News, IReadOnlyList<(int ClubId, string Club, int Count)> ByClub);

/// <summary>
/// Abonnements Web Push et choix des destinataires : buts, coups d'envoi et résultats des équipes suivies,
/// infos importantes, messages manuels de l'organisation.
/// </summary>
public class NotificationService(IAppDbContext db, IPushService push)
{
    public bool Enabled => push.PublicKey is not null;
    public string? PublicKey => push.PublicKey;

    private static bool ValidEndpoint(string e) =>
        Uri.TryCreate(e, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && e.Length <= 1000;

    public async Task SubscribeAsync(SubscriptionInput input, string? userId, string? userAgent, CancellationToken ct = default)
    {
        if (!ValidEndpoint(input.Endpoint) || input.P256dh.Length is 0 or > 200 || input.Auth.Length is 0 or > 100)
            throw new Common.BusinessRuleException("Abonnement invalide.");
        var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == input.Endpoint, ct);
        if (sub is null)
        {
            sub = new PushSubscription { Endpoint = input.Endpoint };
            db.PushSubscriptions.Add(sub);
        }
        sub.P256dh = input.P256dh;
        sub.Auth = input.Auth;
        sub.UserId = userId ?? sub.UserId;
        sub.UserAgent = userAgent is null ? null : userAgent[..Math.Min(300, userAgent.Length)];
        sub.ClubIds = input.ClubIds.Distinct().Take(50).ToList();
        sub.NotifyKickoff = input.NotifyKickoff;
        sub.NotifyGoals = input.NotifyGoals;
        sub.NotifyFullTime = input.NotifyFullTime;
        sub.NotifyNews = input.NotifyNews;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Équipes suivies modifiées sur l'appareil : l'abonnement suit.</summary>
    public async Task UpdateClubsAsync(string endpoint, IEnumerable<int> clubIds, CancellationToken ct = default)
    {
        var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (sub is null) return;
        sub.ClubIds = clubIds.Distinct().Take(50).ToList();
        await db.SaveChangesAsync(ct);
    }

    public async Task<SubscriptionPrefs?> PrefsAsync(string endpoint, CancellationToken ct = default) =>
        await db.PushSubscriptions.AsNoTracking().Where(s => s.Endpoint == endpoint)
            .Select(s => new SubscriptionPrefs(s.NotifyKickoff, s.NotifyGoals, s.NotifyFullTime, s.NotifyNews, s.ClubIds))
            .FirstOrDefaultAsync(ct);

    public Task UnsubscribeAsync(string endpoint, CancellationToken ct = default) =>
        db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ExecuteDeleteAsync(ct);

    // ------------------------------------------------------------ Envois

    /// <summary>Coup d'envoi, but ou fin d'un match : abonnés qui suivent l'une des deux équipes.</summary>
    public async Task MatchAsync(Match m, MatchPushKind kind, string? detail, CancellationToken ct = default)
    {
        if (!Enabled || m.HomeClubId is not { } home || m.AwayClubId is not { } away || m.HomeClub is null || m.AwayClub is null) return;
        var q = db.PushSubscriptions.AsNoTracking().Where(s => s.ClubIds.Contains(home) || s.ClubIds.Contains(away));
        q = kind switch
        {
            MatchPushKind.Kickoff => q.Where(s => s.NotifyKickoff),
            MatchPushKind.Goal => q.Where(s => s.NotifyGoals),
            _ => q.Where(s => s.NotifyFullTime)
        };
        var ids = await q.Select(s => s.Id).ToListAsync(ct);
        if (ids.Count == 0) return;

        var (h, a) = (m.HomeClub.ShortName, m.AwayClub.ShortName);
        var score = $"{h} {m.HomeScore ?? 0}-{m.AwayScore ?? 0} {a}";
        var pens = m.HomePenalties is { } hp && m.AwayPenalties is { } ap ? $" ({hp}-{ap} aux tirs au but)" : "";
        var message = kind switch
        {
            MatchPushKind.Kickoff => new PushMessage("Coup d'envoi", $"{m.HomeClub.Name} - {m.AwayClub.Name}", Mapping.MatchUrl(m), $"match-{m.Id}", 1800),
            MatchPushKind.Goal => new PushMessage($"But ! {score}", detail ?? "", Mapping.MatchUrl(m), $"match-{m.Id}", 1800),
            _ => new PushMessage($"Terminé : {score}{pens}", detail ?? "Résultat final", Mapping.MatchUrl(m), $"match-{m.Id}", 6 * 3600)
        };
        push.Enqueue(message, ids);
    }

    /// <summary>Info importante mise en ligne : envoyée une seule fois aux abonnés « infos ».</summary>
    public async Task ArticleAsync(int articleId, CancellationToken ct = default)
    {
        if (!Enabled) return;
        var a = await db.Articles.FirstOrDefaultAsync(x => x.Id == articleId, ct);
        if (a is null || !a.IsImportant || a.NotifiedAt is not null) return;
        if (!(a.Status is ArticleStatus.Published or ArticleStatus.Scheduled && a.PublishedAt <= DateTimeOffset.UtcNow)) return;
        a.NotifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var ids = await db.PushSubscriptions.AsNoTracking().Where(s => s.NotifyNews).Select(s => s.Id).ToListAsync(ct);
        if (ids.Count > 0)
            push.Enqueue(new PushMessage(a.Title, a.Summary ?? "Nouvelle info importante", $"/infos/{a.Slug}", $"info-{a.Id}", 24 * 3600), ids);
    }

    /// <summary>Infos importantes programmées dont l'heure vient de passer (vérifié chaque minute).</summary>
    public async Task NotifyDueArticlesAsync(CancellationToken ct = default)
    {
        if (!Enabled) return;
        var now = DateTimeOffset.UtcNow;
        var due = await db.Articles.AsNoTracking()
            .Where(a => a.IsImportant && a.NotifiedAt == null && a.Status == ArticleStatus.Scheduled
                && a.PublishedAt <= now && a.PublishedAt > now.AddHours(-2))
            .Select(a => a.Id).ToListAsync(ct);
        foreach (var id in due) await ArticleAsync(id, ct);
    }

    /// <summary>Message de l'organisation : tous les abonnés, ou ceux d'une équipe.</summary>
    public async Task<int> ManualAsync(string title, string body, string? url, int? clubId, CancellationToken ct = default)
    {
        if (!Enabled) throw new Common.BusinessRuleException("Les notifications ne sont pas configurées sur ce serveur.");
        var q = db.PushSubscriptions.AsNoTracking();
        if (clubId is { } c) q = q.Where(s => s.ClubIds.Contains(c));
        var ids = await q.Select(s => s.Id).ToListAsync(ct);
        var link = string.IsNullOrWhiteSpace(url) || !url.StartsWith('/') ? "/" : url.Trim();
        if (ids.Count > 0) push.Enqueue(new PushMessage(title.Trim(), body.Trim(), link, $"manuel-{DateTime.UtcNow.Ticks}", 24 * 3600), ids);
        return ids.Count;
    }

    public async Task<PushAudience> AudienceAsync(CancellationToken ct = default)
    {
        var subs = await db.PushSubscriptions.AsNoTracking().Select(s => new { s.NotifyNews, s.ClubIds }).ToListAsync(ct);
        var clubIds = subs.SelectMany(s => s.ClubIds).Distinct().ToList();
        var names = await db.Clubs.AsNoTracking().Where(c => clubIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var byClub = subs.SelectMany(s => s.ClubIds).GroupBy(x => x).Where(g => names.ContainsKey(g.Key))
            .Select(g => (g.Key, names[g.Key], g.Count())).OrderByDescending(x => x.Item3).ToList();
        return new PushAudience(subs.Count, subs.Count(s => s.NotifyNews), byClub);
    }
}
