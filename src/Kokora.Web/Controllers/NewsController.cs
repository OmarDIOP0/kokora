using Kokora.Application.Abstractions;
using Kokora.Application.Public;
using Kokora.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Controllers;

/// <summary>Infos : communiqués, résumés, décisions de la commission.</summary>
public class NewsController(NewsService news, Kokora.Application.Engagement.CommentService comments) : Controller
{
    [HttpGet("/infos")]
    public async Task<IActionResult> Index(string? categorie, string? tag, string? equipe, int page = 1, CancellationToken ct = default)
    {
        ViewData["Nav"] = "infos";
        var filter = new NewsFilter(Blank(categorie), Blank(tag), Blank(equipe));
        var data = await news.ListAsync(filter, page, ct: ct);
        // Défilement infini : lignes suivantes seulement.
        if (Request.Headers.ContainsKey("HX-Request") && page > 1) return PartialView("_ArticleRows", data);

        // « À la une » en tête de la première page non filtrée, sans doublon dans la liste.
        var featured = filter.IsEmpty && page == 1 ? await news.FeaturedAsync(ct) : null;
        if (featured is not null) data = data with { Items = data.Items.Where(a => a.Id != featured.Id).ToList() };

        string? label = null;
        if (filter.Tag is { } t) label = await news.TagNameAsync(t, ct) is { } tn ? $"Mot-clé : {tn}" : null;
        else if (filter.Club is { } c) label = await news.ClubNameAsync(c, ct) is { } cn ? $"Équipe : {cn}" : null;
        if ((filter.Tag ?? filter.Club) is not null && label is null) return NotFound();

        return View(new NewsIndexVm
        {
            Filter = filter, Page = data, Featured = featured, Categories = await news.CategoriesAsync(ct), FilterLabel = label
        });
    }

    [HttpGet("/infos/{slug}")]
    public async Task<IActionResult> Article(string slug, CancellationToken ct)
    {
        ViewData["Nav"] = "infos";
        // L'équipe de rédaction peut prévisualiser brouillons et infos programmées.
        var preview = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Editor);
        var article = await news.ArticleAsync(slug, preview, ct);
        if (article is null) return NotFound();
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return View(new ArticlePageVm
        {
            Article = article, ShareUrl = $"{Request.Scheme}://{Request.Host}{article.Card.Url}",
            Comments = new CommentsPartVm(article.Card.Id, article.AllowComments && article.IsLive,
                await comments.ListAsync(article.Card.Id, userId, ct))
        });
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
