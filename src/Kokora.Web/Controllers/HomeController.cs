using System.Diagnostics;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Web.Infrastructure;
using Kokora.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Controllers;

public class HomeController(PublicContext ctx, MatchQueryService queries, StandingsService standings, IAppDbContext db) : Controller
{
    private const int PageSize = 30;

    /// <summary>Accueil = les matchs : journée (bande de dates), à venir ou résultats.</summary>
    [HttpGet("/")]
    public async Task<IActionResult> Index(DateOnly? date, string? c, string? vue, int page = 1, CancellationToken ct = default)
    {
        ViewData["Nav"] = "matchs";
        await ctx.LoadAsync(c, ct);
        var view = vue switch { "a-venir" => MatchesView.Upcoming, "resultats" => MatchesView.Results, _ => MatchesView.Day };
        var day = date ?? KokoraTime.Today;
        if (ctx.Season is null) return View(new HomeVm { Ctx = ctx, View = view, Date = day });

        var seasonId = ctx.Season.Id;
        var compId = ctx.Selected?.Id;
        var isHtmx = Request.Headers.ContainsKey("HX-Request");

        // Pages suivantes (défilement infini) : uniquement les jours suivants.
        if (view != MatchesView.Day && isHtmx && page > 1)
            return PartialView("_MatchList", await List(view, page, ct));

        var live = await queries.LiveAsync(seasonId, compId, ct);
        // Rafraîchissement périodique du bloc « En direct » (en attendant le temps réel de la phase 6).
        if (isHtmx && Request.Headers["HX-Target"] == "live-block")
            return PartialView("_LiveBlock", new HomeVm { Ctx = ctx, View = view, Date = day, Live = live });

        List<DateOnly> strip = [];
        Dictionary<DateOnly, DayInfo> days = [];
        List<MatchGroupVm> groups = [];
        DateOnly? nearest = null;
        MatchListVm? list = null;
        if (view == MatchesView.Day)
        {
            strip = Enumerable.Range(-7, 15).Select(i => day.AddDays(i)).ToList();
            days = (await queries.DaysAsync(seasonId, compId, strip[0], strip[^1], ct)).ToDictionary(d => d.Day);
            groups = await queries.DayAsync(seasonId, day, compId, ctx.Favorites, ct);
            if (groups.Count == 0)
            {
                var around = await queries.DaysAsync(seasonId, compId, day.AddDays(-60), day.AddDays(60), ct);
                nearest = around.OrderBy(d => Math.Abs(d.Day.DayNumber - day.DayNumber)).Select(d => (DateOnly?)d.Day).FirstOrDefault();
            }
        }
        else list = await List(view, 1, ct);

        var (mini, miniUrl) = await MiniStandingsAsync(ct);
        return View(new HomeVm
        {
            Ctx = ctx, View = view, Date = day, Live = live,
            Featured = await queries.NextFeaturedAsync(seasonId, ct),
            FavoriteTeams = await FavoriteTeamsAsync(ct),
            Strip = strip, Days = days, Groups = groups, NearestDay = nearest, List = list,
            MiniStandings = mini, MiniStandingsUrl = miniUrl
        });
    }

    private async Task<MatchListVm> List(MatchesView view, int page, CancellationToken ct)
    {
        var (days, hasMore) = await queries.ListAsync(ctx.Season!.Id, ctx.Selected?.Id, view == MatchesView.Upcoming,
            page, PageSize, ctx.Favorites, ct);
        var next = $"/?vue={(view == MatchesView.Upcoming ? "a-venir" : "resultats")}&page={page + 1}"
                   + (ctx.Selected is { } s ? $"&c={s.Slug}" : "");
        return new MatchListVm(days, hasMore, next);
    }

    /// <summary>Classement résumé : poule d'une équipe suivie, sinon première poule en cours de la compétition choisie.</summary>
    private async Task<(StandingsTableVm?, string?)> MiniStandingsAsync(CancellationToken ct)
    {
        var comps = ctx.Selected is { } sel ? [sel] : ctx.Competitions;
        foreach (var comp in comps)
        {
            var phases = await standings.CompetitionAsync(comp.Id, ct);
            var groups = phases.Where(p => p.Groups.Count > 0).SelectMany(p => p.Groups).ToList();
            var group = groups.FirstOrDefault(g => g.Table.Rows.Any(r => ctx.Favorites.Contains(r.Team.Id)))
                        ?? groups.LastOrDefault(g => g.PlayedMatches > 0) ?? groups.FirstOrDefault();
            if (group is not null)
                return (group.Table with { Compact = true, Favorites = ctx.Favorites }, $"/classements/{comp.Slug}");
        }
        return (null, null);
    }

    private async Task<List<TeamVm>> FavoriteTeamsAsync(CancellationToken ct)
    {
        if (ctx.Favorites.Count == 0) return [];
        var ids = ctx.Favorites.ToList();
        return await db.Clubs.AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Name)
            .Select(c => new TeamVm(c.Id, c.Name, c.ShortName, c.Slug, c.PrimaryColor, c.SecondaryColor, c.LogoPath))
            .ToListAsync(ct);
    }

    [HttpGet("/plus")]
    public IActionResult More()
    {
        ViewData["Nav"] = "plus";
        return View();
    }

    /// <summary>Page affichée par le service worker quand une page jamais visitée est demandée sans réseau.</summary>
    [HttpGet("/hors-ligne")]
    public IActionResult Offline() => View();

    // Toutes méthodes : la page d'erreur est rejouée avec la méthode de la requête d'origine (ex. POST).
    [Route("/erreur")]
    [IgnoreAntiforgeryToken] // la page d'erreur doit toujours pouvoir s'afficher, même après un formulaire expiré
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? code)
    {
        ViewData["RequestId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        ViewData["Code"] = code;
        return View();
    }
}
