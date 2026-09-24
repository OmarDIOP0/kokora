using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Domain.Content;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Public;

public record ArticleCardVm(int Id, string Title, string Slug, string? Summary, string? CoverUrl, string? CoverSmallUrl,
    string? Category, string? CategorySlug, DateTimeOffset PublishedAt, bool IsFeatured, bool IsImportant, int ReadMinutes)
{
    public string Url => $"/infos/{Slug}";
}

public record CategoryVm(string Name, string Slug, int Count);

public record TagVm(string Name, string Slug);

public record GalleryPhotoVm(int Id, string Url, string ThumbUrl, int Width, int Height, string? Caption, string? Credit);

public record NewsFilter(string? Category = null, string? Tag = null, string? Club = null)
{
    public bool IsEmpty => Category is null && Tag is null && Club is null;

    public string Query(int page) =>
        "/infos?" + string.Join("&", new[]
        {
            Category is null ? null : $"categorie={Uri.EscapeDataString(Category)}",
            Tag is null ? null : $"tag={Uri.EscapeDataString(Tag)}",
            Club is null ? null : $"equipe={Uri.EscapeDataString(Club)}",
            $"page={page}"
        }.Where(x => x is not null));
}

public record NewsPageData(IReadOnlyList<ArticleCardVm> Items, bool HasMore, string NextUrl);

public record ArticleDetailVm
{
    public required ArticleCardVm Card { get; init; }
    public required string BodyHtml { get; init; }
    /// <summary>Chapeau rédigé (null si le résumé est tiré du texte, pour ne pas le répéter).</summary>
    public string? Lead { get; init; }
    public ArticleStatus Status { get; init; }
    public bool IsLive { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? AuthorName { get; init; }
    public bool AllowComments { get; init; }
    public IReadOnlyList<TagVm> Tags { get; init; } = [];
    public IReadOnlyList<TeamVm> Teams { get; init; } = [];
    public IReadOnlyList<MatchRowVm> Matches { get; init; } = [];
    public IReadOnlyList<GalleryPhotoVm> Photos { get; init; } = [];
    public IReadOnlyList<ArticleCardVm> Related { get; init; } = [];
}

/// <summary>Infos publiques : liste, filtres, article, infos liées à une équipe ou à un match.</summary>
public class NewsService(IAppDbContext db, IHtmlCleaner cleaner)
{
    public const int PageSize = 15;

    /// <summary>Infos en ligne : publiées, ou programmées dont l'heure est passée.</summary>
    public static IQueryable<Article> Live(IQueryable<Article> q, DateTimeOffset now) =>
        q.Where(a => (a.Status == ArticleStatus.Published || a.Status == ArticleStatus.Scheduled) && a.PublishedAt <= now);

    private IQueryable<Article> LiveArticles => Live(db.Articles.AsNoTracking(), DateTimeOffset.UtcNow);

    public async Task<NewsPageData> ListAsync(NewsFilter filter, int page, int? excludeId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var q = LiveArticles;
        if (filter.Category is { } c) q = q.Where(a => a.Category != null && a.Category.Slug == c);
        if (filter.Tag is { } t) q = q.Where(a => a.Tags.Any(x => x.Slug == t));
        if (filter.Club is { } club) q = q.Where(a => a.Clubs.Any(x => x.Slug == club));
        if (excludeId is { } ex) q = q.Where(a => a.Id != ex);
        var rows = await Cards(q.OrderByDescending(a => a.PublishedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * PageSize).Take(PageSize + 1), ct);
        return new NewsPageData(rows.Take(PageSize).ToList(), rows.Count > PageSize, filter.Query(page + 1));
    }

    /// <summary>« À la une » : la dernière info mise en avant (des 30 derniers jours), sinon rien.</summary>
    public async Task<ArticleCardVm?> FeaturedAsync(CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        return (await Cards(LiveArticles.Where(a => a.IsFeatured && a.PublishedAt >= since)
            .OrderByDescending(a => a.PublishedAt).Take(1), ct)).FirstOrDefault();
    }

    /// <summary>Catégories ayant au moins une info en ligne.</summary>
    public async Task<List<CategoryVm>> CategoriesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.ArticleCategories.AsNoTracking().OrderBy(c => c.Order).ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Name, c.Slug,
                Count = db.Articles.Count(a => a.CategoryId == c.Id
                    && (a.Status == ArticleStatus.Published || a.Status == ArticleStatus.Scheduled) && a.PublishedAt <= now)
            })
            .ToListAsync(ct);
        return rows.Where(r => r.Count > 0).Select(r => new CategoryVm(r.Name, r.Slug, r.Count)).ToList();
    }

    public Task<string?> TagNameAsync(string slug, CancellationToken ct = default) =>
        db.Tags.AsNoTracking().Where(t => t.Slug == slug).Select(t => t.Name).FirstOrDefaultAsync(ct);

    public Task<string?> ClubNameAsync(string slug, CancellationToken ct = default) =>
        db.Clubs.AsNoTracking().Where(c => c.Slug == slug).Select(c => c.Name).FirstOrDefaultAsync(ct);

    /// <summary>Info complète. <paramref name="preview"/> : l'équipe de rédaction voit aussi les brouillons.</summary>
    public async Task<ArticleDetailVm?> ArticleAsync(string slug, bool preview, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = preview ? db.Articles.AsNoTracking() : LiveArticles;
        var a = await q.Include(x => x.Category).Include(x => x.Tags).Include(x => x.Clubs).Include(x => x.Matches)
            .AsSplitQuery().FirstOrDefaultAsync(x => x.Slug == slug, ct);
        if (a is null) return null;

        var matchIds = a.Matches.Select(m => m.Id).ToList();
        var matches = (await db.Matches.Where(m => matchIds.Contains(m.Id) && m.Phase.Competition.IsPublished).WithRowData().ToListAsync(ct))
            .OrderBy(m => m.KickoffAt).Select(m => Mapping.Row(m, now)).ToList();
        var photos = await db.Photos.AsNoTracking().Where(p => p.ArticleId == a.Id).OrderBy(p => p.Order).ThenBy(p => p.Id)
            .Select(p => new GalleryPhotoVm(p.Id, p.Path, p.ThumbnailPath ?? p.Path, p.Width, p.Height, p.Caption, p.Credit))
            .ToListAsync(ct);

        // Infos liées : mêmes équipes d'abord, sinon même catégorie.
        var clubIds = a.Clubs.Select(c => c.Id).ToList();
        var related = await Cards(LiveArticles.Where(x => x.Id != a.Id)
            .OrderByDescending(x => x.Clubs.Any(c => clubIds.Contains(c.Id)))
            .ThenByDescending(x => x.CategoryId == a.CategoryId)
            .ThenByDescending(x => x.PublishedAt).Take(3), ct);

        return new ArticleDetailVm
        {
            Card = Card(a),
            BodyHtml = a.Body,
            Lead = a.Summary,
            Status = a.Status,
            IsLive = ArticleAdminService.IsLive(a, now),
            UpdatedAt = a.UpdatedAt > (a.PublishedAt ?? a.CreatedAt).AddMinutes(30) ? a.UpdatedAt : null,
            AuthorName = a.AuthorName,
            AllowComments = a.AllowComments,
            Tags = a.Tags.OrderBy(t => t.Name).Select(t => new TagVm(t.Name, t.Slug)).ToList(),
            Teams = a.Clubs.OrderBy(c => c.Name).Select(c => Mapping.Team(c)!).ToList(),
            Matches = matches,
            Photos = photos,
            Related = related
        };
    }

    public async Task<List<ArticleCardVm>> ForClubAsync(int clubId, int take, CancellationToken ct = default) =>
        await Cards(LiveArticles.Where(a => a.Clubs.Any(c => c.Id == clubId)).OrderByDescending(a => a.PublishedAt).Take(take), ct);

    public async Task<List<ArticleCardVm>> ForMatchAsync(int matchId, CancellationToken ct = default) =>
        await Cards(LiveArticles.Where(a => a.Matches.Any(m => m.Id == matchId)).OrderByDescending(a => a.PublishedAt).Take(5), ct);

    public Task<List<GalleryPhotoVm>> MatchPhotosAsync(int matchId, CancellationToken ct = default) =>
        db.Photos.AsNoTracking().Where(p => p.MatchId == matchId).OrderBy(p => p.Order).ThenBy(p => p.Id)
            .Select(p => new GalleryPhotoVm(p.Id, p.Path, p.ThumbnailPath ?? p.Path, p.Width, p.Height, p.Caption, p.Credit))
            .ToListAsync(ct);

    /// <summary>Dernières infos (accueil).</summary>
    public async Task<List<ArticleCardVm>> LatestAsync(int take, CancellationToken ct = default) =>
        await Cards(LiveArticles.OrderByDescending(a => a.PublishedAt).Take(take), ct);

    private async Task<List<ArticleCardVm>> Cards(IQueryable<Article> q, CancellationToken ct) =>
        (await q.Include(a => a.Category).ToListAsync(ct)).Select(Card).ToList();

    /// <summary>Début du texte coupé à la fin d'un mot.</summary>
    public static string Excerpt(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', max - 2);
        return text[..(cut < max / 2 ? max - 2 : cut)].TrimEnd(',', ';', ':', '.') + "…";
    }

    private ArticleCardVm Card(Article a)
    {
        var text = cleaner.ToPlainText(a.Body);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var summary = a.Summary ?? Excerpt(text, 180);
        return new ArticleCardVm(a.Id, a.Title, a.Slug, string.IsNullOrEmpty(summary) ? null : summary,
            a.CoverImagePath, ArticleAdminService.CoverSmall(a.CoverImagePath), a.Category?.Name, a.Category?.Slug,
            a.PublishedAt ?? a.UpdatedAt ?? a.CreatedAt, a.IsFeatured, a.IsImportant, Math.Max(1, (int)Math.Round(words / 200.0)));
    }
}
