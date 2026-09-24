using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Content;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

/// <summary>Rédaction des infos : articles, catégories, mots-clés, images.</summary>
public class ArticleAdminService(IAppDbContext db, IImageStore images, IHtmlCleaner cleaner, ICurrentUser user,
    Engagement.NotificationService notifications)
{
    public const int PageSize = 30;

    // ------------------------------------------------------------ Liste

    public async Task<(List<ArticleListItem> Items, int Total)> ListAsync(ArticleStatus? status, string? search, int page,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = db.Articles.AsNoTracking();
        q = status switch
        {
            ArticleStatus.Published => q.Where(a => a.Status == ArticleStatus.Published || (a.Status == ArticleStatus.Scheduled && a.PublishedAt <= now)),
            ArticleStatus.Scheduled => q.Where(a => a.Status == ArticleStatus.Scheduled && a.PublishedAt > now),
            { } s => q.Where(a => a.Status == s),
            null => q
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(a => a.Title.ToLower().Contains(s));
        }
        var total = await q.CountAsync(ct);
        var items = await q
            // Brouillons récents d'abord, puis par date de publication.
            .OrderByDescending(a => a.PublishedAt ?? a.UpdatedAt ?? a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((Math.Max(1, page) - 1) * PageSize).Take(PageSize)
            .Select(a => new ArticleListItem(a.Id, a.Title, a.Slug, a.Status, a.PublishedAt, a.UpdatedAt ?? a.CreatedAt,
                a.Category != null ? a.Category.Name : null, a.CoverImagePath, a.IsFeatured, a.IsImportant, a.AuthorName, a.IsDemo,
                db.Photos.Count(p => p.ArticleId == a.Id)))
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<ArticleCounts> CountsAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Articles.AsNoTracking()
            .Select(a => new { a.Status, Live = a.PublishedAt <= now }).ToListAsync(ct);
        return new ArticleCounts(rows.Count,
            rows.Count(r => r.Status == ArticleStatus.Draft),
            rows.Count(r => r.Status == ArticleStatus.Scheduled && !r.Live),
            rows.Count(r => r.Status == ArticleStatus.Published || (r.Status == ArticleStatus.Scheduled && r.Live)),
            rows.Count(r => r.Status == ArticleStatus.Archived));
    }

    // ------------------------------------------------------------ Formulaire

    public async Task<Article> GetAsync(int id, CancellationToken ct = default) =>
        await db.Articles.Include(a => a.Tags).Include(a => a.Clubs).Include(a => a.Matches).Include(a => a.Category)
            .AsSplitQuery().FirstOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("Info");

    public static ArticleInput ToInput(Article a) => new()
    {
        Id = a.Id, Title = a.Title, Slug = a.Slug, Summary = a.Summary, Body = a.Body, CategoryId = a.CategoryId,
        Tags = a.Tags.Select(t => t.Id.ToString()).ToList(),
        ClubIds = a.Clubs.Select(c => c.Id).ToList(),
        MatchIds = a.Matches.Select(m => m.Id).ToList(),
        Mode = a.Status switch
        {
            ArticleStatus.Published => PublishMode.Now,
            ArticleStatus.Scheduled => PublishMode.Scheduled,
            ArticleStatus.Archived => PublishMode.Archived,
            _ => PublishMode.Draft
        },
        PublishAtLocal = a.PublishedAt is { } p ? KokoraTime.ToLocal(p).DateTime : null,
        IsFeatured = a.IsFeatured, IsImportant = a.IsImportant, AllowComments = a.AllowComments
    };

    public async Task<int> SaveAsync(ArticleInput input, Stream? cover, CancellationToken ct = default)
    {
        var title = input.Title.Trim();
        var body = cleaner.Clean(input.Body);
        var now = DateTimeOffset.UtcNow;

        DateTimeOffset? scheduledAt = null;
        if (input.Mode == PublishMode.Scheduled)
        {
            if (input.PublishAtLocal is not { } local)
                throw new BusinessRuleException("Choisissez la date et l'heure de publication.", nameof(input.PublishAtLocal));
            scheduledAt = ScheduleService.ToUtc(local);
            if (scheduledAt <= now.AddMinutes(1))
                throw new BusinessRuleException("La date de publication programmée doit être dans le futur.", nameof(input.PublishAtLocal));
        }
        if (input.Mode is PublishMode.Now or PublishMode.Scheduled && cleaner.ToPlainText(body).Length == 0)
            throw new BusinessRuleException("Le texte de l'info est vide : impossible de la publier.", nameof(input.Body));
        if (input.CategoryId is { } catId && !await db.ArticleCategories.AnyAsync(c => c.Id == catId, ct))
            throw new BusinessRuleException("Catégorie introuvable.", nameof(input.CategoryId));

        var article = input.Id == 0 ? new Article() : await GetAsync(input.Id, ct);
        var wasLive = article.Id != 0 && IsLive(article, now);

        article.Title = title;
        article.Summary = Clean(input.Summary);
        article.Body = body;
        article.CategoryId = input.CategoryId;
        article.IsFeatured = input.IsFeatured;
        article.IsImportant = input.IsImportant;
        article.AllowComments = input.AllowComments;

        switch (input.Mode)
        {
            case PublishMode.Now:
                article.Status = ArticleStatus.Published;
                // Une info déjà en ligne garde sa date ; sinon elle est publiée maintenant.
                if (!wasLive || article.PublishedAt is null || article.PublishedAt > now) article.PublishedAt = now;
                break;
            case PublishMode.Scheduled:
                article.Status = ArticleStatus.Scheduled;
                article.PublishedAt = scheduledAt;
                break;
            case PublishMode.Archived:
                article.Status = ArticleStatus.Archived;
                break;
            default:
                article.Status = ArticleStatus.Draft;
                article.PublishedAt = null;
                break;
        }

        // Adresse : fixée à la création ; modifiable ensuite (unique).
        var wantedSlug = Clean(input.Slug);
        if (article.Id == 0 || (wantedSlug is not null && wantedSlug != article.Slug))
        {
            var baseText = wantedSlug ?? title;
            if (wantedSlug is not null && await db.Articles.AnyAsync(a => a.Id != article.Id && a.Slug == wantedSlug, ct))
                throw new BusinessRuleException("Cette adresse est déjà utilisée par une autre info.", nameof(input.Slug));
            article.Slug = wantedSlug ?? await Slug.UniqueAsync(Slug.From(baseText, 100), s => db.Articles.AnyAsync(a => a.Slug == s, ct));
        }

        if (article.Id == 0)
        {
            article.AuthorId = user.UserId;
            article.AuthorName = user.UserName;
            db.Articles.Add(article);
        }

        article.Tags = await ResolveTagsAsync(input.Tags, ct);
        var clubIds = input.ClubIds.Distinct().ToList();
        article.Clubs = await db.Clubs.Where(c => clubIds.Contains(c.Id)).ToListAsync(ct);
        var matchIds = input.MatchIds.Distinct().ToList();
        article.Matches = await db.Matches.Where(m => matchIds.Contains(m.Id)).ToListAsync(ct);

        string? oldCover = null;
        if (cover is not null)
        {
            var urls = await images.SaveAsync(cover, "infos", Slug.From(title, 40), ImagePresets.Cover, ct);
            oldCover = article.CoverImagePath;
            article.CoverImagePath = urls[0];
        }
        else if (input.RemoveCover)
        {
            oldCover = article.CoverImagePath;
            article.CoverImagePath = null;
        }

        await db.SaveChangesAsync(ct);
        await DeleteCoverAsync(oldCover);
        // Info importante en ligne : notification (une seule fois). Programmée : envoyée à l'heure par la tâche de fond.
        if (article.IsImportant) await notifications.ArticleAsync(article.Id, ct);
        return article.Id;
    }

    public static bool IsLive(Article a, DateTimeOffset now) =>
        a.Status is ArticleStatus.Published or ArticleStatus.Scheduled && a.PublishedAt <= now;

    /// <summary>Mots-clés : identifiants existants, ou texte libre créant un nouveau mot-clé (dédoublonné par slug).</summary>
    private async Task<List<Tag>> ResolveTagsAsync(IEnumerable<string> values, CancellationToken ct)
    {
        var ids = new List<int>();
        var names = new List<string>();
        foreach (var v in values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()))
        {
            if (int.TryParse(v, out var id)) ids.Add(id);
            else if (v.Length <= 60) names.Add(v);
        }
        var tags = await db.Tags.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        foreach (var name in names.DistinctBy(n => Slug.From(n)))
        {
            var slug = Slug.From(name, 60);
            if (slug.Length == 0 || tags.Any(t => t.Slug == slug)) continue;
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug, ct)
                      ?? db.Tags.Local.FirstOrDefault(t => t.Slug == slug);
            if (tag is null)
            {
                tag = new Tag { Name = name, Slug = slug };
                db.Tags.Add(tag);
            }
            tags.Add(tag);
        }
        return tags.Take(12).ToList();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var article = await GetAsync(id, ct);
        var photos = await db.Photos.Where(p => p.ArticleId == id).ToListAsync(ct);
        db.Articles.Remove(article);
        await db.SaveChangesAsync(ct);
        await DeleteCoverAsync(article.CoverImagePath);
        foreach (var p in photos) { await images.DeleteAsync(p.Path); await images.DeleteAsync(p.ThumbnailPath); }
        await DeleteOrphanTagsAsync(ct);
    }

    private async Task DeleteOrphanTagsAsync(CancellationToken ct) =>
        await db.Tags.Where(t => !t.Articles.Any()).ExecuteDeleteAsync(ct);

    private async Task DeleteCoverAsync(string? cover)
    {
        if (cover is null) return;
        await images.DeleteAsync(cover);
        // Variante « -sm » créée avec la couverture.
        await images.DeleteAsync(CoverSmall(cover));
    }

    /// <summary>Vignette 640×360 enregistrée à côté de la couverture.</summary>
    public static string? CoverSmall(string? cover) =>
        cover is null ? null : cover.EndsWith(".webp", StringComparison.Ordinal) ? cover[..^5] + "-sm.webp" : cover;

    /// <summary>Image insérée dans le texte depuis l'éditeur.</summary>
    public async Task<string> UploadInlineImageAsync(Stream file, CancellationToken ct = default) =>
        (await images.SaveAsync(file, "infos", "image", ImagePresets.Inline, ct))[0];

    public Task<List<(int Id, string Name)>> TagsAsync(CancellationToken ct = default) =>
        db.Tags.AsNoTracking().OrderBy(t => t.Name).Select(t => new ValueTuple<int, string>(t.Id, t.Name)).ToListAsync(ct);

    // ------------------------------------------------------------ Catégories

    public Task<List<CategoryItem>> CategoriesAsync(CancellationToken ct = default) =>
        db.ArticleCategories.AsNoTracking().OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Select(c => new CategoryItem(c.Id, c.Name, c.Slug, db.Articles.Count(a => a.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<int> SaveCategoryAsync(CategoryInput input, CancellationToken ct = default)
    {
        var name = input.Name.Trim();
        if (await db.ArticleCategories.AnyAsync(c => c.Id != input.Id && c.Name.ToLower() == name.ToLower(), ct))
            throw new BusinessRuleException("Cette catégorie existe déjà.", nameof(input.Name));
        var cat = input.Id == 0 ? new ArticleCategory() : await db.ArticleCategories.FindAsync([input.Id], ct) ?? throw new NotFoundException("Catégorie");
        cat.Name = name;
        if (input.Id == 0)
        {
            cat.Slug = await Slug.UniqueAsync(name, s => db.ArticleCategories.AnyAsync(c => c.Slug == s, ct));
            cat.Order = (await db.ArticleCategories.MaxAsync(c => (int?)c.Order, ct) ?? 0) + 1;
            db.ArticleCategories.Add(cat);
        }
        await db.SaveChangesAsync(ct);
        return cat.Id;
    }

    /// <summary>Les infos de la catégorie restent, simplement sans catégorie.</summary>
    public async Task DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        var cat = await db.ArticleCategories.FindAsync([id], ct) ?? throw new NotFoundException("Catégorie");
        db.ArticleCategories.Remove(cat);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderCategoriesAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        var cats = await db.ArticleCategories.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        foreach (var c in cats) c.Order = ids.ToList().IndexOf(c.Id) + 1;
        await db.SaveChangesAsync(ct);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
