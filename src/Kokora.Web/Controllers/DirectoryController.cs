using Kokora.Application.Public;
using Kokora.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Controllers;

public class StatsPageVm
{
    public required PublicContext Ctx { get; init; }
    public StatsPageData? Data { get; init; }
}

/// <summary>Statistiques, équipes, joueurs et recherche.</summary>
public class DirectoryController(PublicContext ctx, StatsService stats, DirectoryService directory) : Controller
{
    [HttpGet("/stats")]
    public async Task<IActionResult> Stats(string? c, CancellationToken ct)
    {
        ViewData["Nav"] = "stats";
        await ctx.LoadAsync(c, ct);
        var data = ctx.Season is null ? null : await stats.GetAsync(ctx.Season.Id, ctx.Selected?.Id, ct);
        return View(new StatsPageVm { Ctx = ctx, Data = data });
    }

    [HttpGet("/equipes")]
    public async Task<IActionResult> Teams(CancellationToken ct)
    {
        ViewData["Nav"] = "equipes";
        return View(await directory.TeamsAsync(ct));
    }

    [HttpGet("/equipes/{slug}")]
    public async Task<IActionResult> Team(string slug, CancellationToken ct)
    {
        ViewData["Nav"] = "equipes";
        var data = await directory.TeamAsync(slug, ct);
        return data is null ? NotFound() : View(data);
    }

    [HttpGet("/joueurs/{slug}")]
    public async Task<IActionResult> Player(string slug, CancellationToken ct)
    {
        ViewData["Nav"] = "equipes";
        var data = await directory.PlayerAsync(slug, ct);
        return data is null ? NotFound() : View(data);
    }

    [HttpGet("/recherche")]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        ViewData["Nav"] = "recherche";
        ViewData["Query"] = q;
        var results = await directory.SearchAsync(q, ct);
        return Request.Headers.ContainsKey("HX-Request") ? PartialView("_SearchResults", results) : View(results);
    }
}
