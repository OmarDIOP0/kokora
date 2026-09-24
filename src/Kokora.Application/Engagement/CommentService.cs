using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Domain.Content;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Engagement;

public record CommentVm(int Id, string Author, string Body, DateTimeOffset At, CommentStatus Status, bool IsMine);

public record ModerationItem(int Id, string Author, string UserId, string Body, DateTimeOffset At, CommentStatus Status,
    int ArticleId, string ArticleTitle, string ArticleSlug);

/// <summary>
/// Commentaires des infos. Modération a priori : un commentaire attend la validation de l'équipe,
/// sauf pour l'équipe elle-même et les supporters qui ont déjà 3 commentaires validés (publication immédiate).
/// </summary>
public class CommentService(IAppDbContext db, ICurrentUser current)
{
    public const int TrustedAfter = 3;
    public const int MaxPerTenMinutes = 5;

    public async Task<List<CommentVm>> ListAsync(int articleId, string? userId, CancellationToken ct = default) =>
        (await db.Comments.AsNoTracking()
            .Where(c => c.ArticleId == articleId && (c.Status == CommentStatus.Approved || (userId != null && c.UserId == userId && c.Status == CommentStatus.Pending)))
            .OrderBy(c => c.CreatedAt).Take(300).ToListAsync(ct))
        .Select(c => new CommentVm(c.Id, c.UserDisplayName, c.Body, c.CreatedAt, c.Status, c.UserId == userId)).ToList();

    public async Task<CommentStatus> PostAsync(int articleId, string userId, string displayName, string body, CancellationToken ct = default)
    {
        body = (body ?? "").Trim();
        if (body.Length < 2) throw new BusinessRuleException("Le commentaire est vide.", "Body");
        if (body.Length > 1000) throw new BusinessRuleException("1000 caractères maximum.", "Body");
        var now = DateTimeOffset.UtcNow;
        var article = await NewsService.Live(db.Articles.AsNoTracking(), now).FirstOrDefaultAsync(a => a.Id == articleId, ct)
            ?? throw new NotFoundException("Info");
        if (!article.AllowComments) throw new BusinessRuleException("Les commentaires sont fermés pour cette info.");
        var recent = await db.Comments.CountAsync(c => c.UserId == userId && c.CreatedAt > now.AddMinutes(-10), ct);
        if (recent >= MaxPerTenMinutes) throw new BusinessRuleException("Vous avez beaucoup commenté : patientez quelques minutes.");

        var trusted = current.IsInRole(Roles.SuperAdmin) || current.IsInRole(Roles.Admin) || current.IsInRole(Roles.Editor)
            || await db.Comments.CountAsync(c => c.UserId == userId && c.Status == CommentStatus.Approved, ct) >= TrustedAfter;
        var comment = new Comment
        {
            ArticleId = articleId, UserId = userId, UserDisplayName = displayName, Body = body,
            Status = trusted ? CommentStatus.Approved : CommentStatus.Pending
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment.Status;
    }

    /// <summary>L'auteur peut supprimer son propre commentaire.</summary>
    public async Task DeleteOwnAsync(int id, string userId, CancellationToken ct = default)
    {
        var n = await db.Comments.Where(c => c.Id == id && c.UserId == userId).ExecuteDeleteAsync(ct);
        if (n == 0) throw new NotFoundException("Commentaire");
    }

    public Task<int> PendingCountAsync(CancellationToken ct = default) =>
        db.Comments.CountAsync(c => c.Status == CommentStatus.Pending, ct);

    /// <summary>En attente : les plus anciens d'abord ; sinon les plus récents.</summary>
    public Task<List<ModerationItem>> ModerationAsync(CommentStatus status, int take = 100, CancellationToken ct = default)
    {
        var q = db.Comments.AsNoTracking().Where(c => c.Status == status);
        q = status == CommentStatus.Pending ? q.OrderBy(c => c.CreatedAt) : q.OrderByDescending(c => c.CreatedAt);
        return q.Take(take).Select(c => new ModerationItem(c.Id, c.UserDisplayName, c.UserId, c.Body, c.CreatedAt, c.Status, c.ArticleId, c.Article.Title, c.Article.Slug))
            .ToListAsync(ct);
    }

    public async Task SetStatusAsync(int id, CommentStatus status, CancellationToken ct = default)
    {
        var c = await db.Comments.FindAsync([id], ct) ?? throw new NotFoundException("Commentaire");
        c.Status = status;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Comments.FindAsync([id], ct) ?? throw new NotFoundException("Commentaire");
        db.Comments.Remove(c);
        await db.SaveChangesAsync(ct);
    }
}
