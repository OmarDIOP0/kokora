using Kokora.Application.Public;
using Kokora.Web.Infrastructure;
using Kokora.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Controllers;

public class PublicMatchesController(MatchQueryService queries, StandingsService standings, PublicContext ctx, NewsService news,
    Kokora.Application.Engagement.PredictionService predictions, Kokora.Application.Engagement.VoteService votes) : Controller
{
    private string? UserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    // Un seul segment « 12-demo-1-demo-2 » : ASP.NET découperait « {id}-{slug} » au dernier tiret.
    [HttpGet("/matchs/{key:regex(^[[0-9]]+(-[[a-z0-9-]]+)?$)}")]
    public async Task<IActionResult> Match(string key, CancellationToken ct)
    {
        var id = int.Parse(key.Split('-')[0]);
        var detail = await queries.DetailAsync(id, ct);
        if (detail is null) return NotFound();
        // Adresse canonique (slug à jour si les équipes ont changé).
        if (Request.Path != detail.Match.Url) return RedirectPermanent(detail.Match.Url);

        ViewData["Nav"] = "matchs";
        var m = detail.Match;
        var home = m.Home?.Name ?? m.HomePlaceholder;
        var away = m.Away?.Name ?? m.AwayPlaceholder;
        var score = m.ShowScore ? $"{home} {m.HomeScore}-{m.AwayScore} {away}" : $"{home} - {away}";
        if (m.HomePenalties is not null) score += $" ({m.HomePenalties}-{m.AwayPenalties} tab)";
        var url = $"{Request.Scheme}://{Request.Host}{m.Url}";
        return View(new MatchPageVm
        {
            Detail = detail,
            ShareUrl = url,
            ShareText = $"{score} · {detail.Competition.Name}, navétane de Nguékokh",
            Photos = await news.MatchPhotosAsync(id, ct),
            News = await news.ForMatchAsync(id, ct),
            Prediction = await predictions.BlockAsync(id, UserId, ct),
            Vote = await votes.BlockAsync(id, UserId, ct)
        });
    }

    /// <summary>En-tête et chronologie d'un match (rafraîchissement en direct de la fiche).</summary>
    [HttpGet("/matchs/{id:int}/direct")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LiveFragments(int id, CancellationToken ct)
    {
        var detail = await queries.DetailAsync(id, ct);
        return detail is null ? NotFound() : PartialView("_LiveFragments", detail);
    }

    /// <summary>Une ligne de match, telle qu'affichée dans les listes (rafraîchissement en direct).</summary>
    [HttpGet("/matchs/{id:int}/ligne")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Row(int id, bool large, CancellationToken ct)
    {
        var row = await queries.RowAsync(id, ct);
        if (row is null) return NotFound();
        ViewData["Wide"] = large;
        return PartialView("Components/_MatchRow", row);
    }

    [HttpGet("/classements")]
    [HttpGet("/classements/{slug}")]
    public async Task<IActionResult> Standings(string? slug, int? phase, CancellationToken ct)
    {
        ViewData["Nav"] = "classements";
        await ctx.LoadAsync(slug, ct);
        var comp = ctx.Selected ?? ctx.Competitions.FirstOrDefault();
        if (comp is null) return View("NoStandings", ctx);
        if (slug is null) return Redirect($"/classements/{comp.Slug}");

        var phases = await standings.CompetitionAsync(comp.Id, ct);
        // Phase affichée par défaut : la plus avancée qui a déjà des matchs joués.
        var current = phases.FirstOrDefault(p => p.PhaseId == phase)
            ?? phases.LastOrDefault(p => p.Groups.Any(g => g.PlayedMatches > 0)
                || (p.Bracket?.Rounds.Any(r => r.Matches.Any(x => x.ShowScore || x.Home is not null)) ?? false))
            ?? phases.FirstOrDefault();
        return View(new StandingsPageVm { Ctx = ctx, Competition = comp, Phases = phases, Current = current });
    }
}
