using System.Text;
using System.Xml.Linq;
using Kokora.Application.Abstractions;
using Kokora.Application.Public;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Kokora.Web.Controllers;

/// <summary>Référencement : robots.txt et plan du site (équipes, joueurs, matchs de la saison, infos).</summary>
public class SeoController(IAppDbContext db, MatchQueryService matches, IMemoryCache cache) : Controller
{
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    [HttpGet("/robots.txt")]
    [ResponseCache(Duration = 86400)]
    public ContentResult Robots() => Content($"""
        User-agent: *
        Disallow: /admin
        Disallow: /compte
        Disallow: /direct/
        Disallow: /notifications
        Disallow: /recherche
        Disallow: /_snapshots
        Disallow: /hors-ligne

        Sitemap: {BaseUrl}/sitemap.xml
        """, "text/plain; charset=utf-8");

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Sitemap(CancellationToken ct)
    {
        var xml = await cache.GetOrCreateAsync($"sitemap:{BaseUrl}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await BuildAsync(ct);
        });
        return Content(xml!, "application/xml; charset=utf-8");
    }

    private async Task<string> BuildAsync(CancellationToken ct)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = new List<(string Path, DateTimeOffset? Modified, string Frequency)>
        {
            ("/", null, "hourly"), ("/classements", null, "daily"), ("/stats", null, "daily"),
            ("/equipes", null, "weekly"), ("/infos", null, "daily"), ("/pronostics", null, "daily")
        };
        var season = await matches.CurrentSeasonAsync(ct);
        if (season is not null)
        {
            foreach (var c in await matches.CompetitionsAsync(season.Id, ct)) urls.Add(($"/classements/{c.Slug}", null, "daily"));
            var seasonMatches = await db.Matches.AsNoTracking()
                .Where(m => m.Phase.Competition.SeasonId == season.Id && m.Phase.Competition.IsPublished && m.HomeClubId != null && m.AwayClubId != null)
                .Select(m => new { m.Id, Home = m.HomeClub!.ShortName, Away = m.AwayClub!.ShortName, Modified = m.UpdatedAt ?? m.CreatedAt })
                .ToListAsync(ct);
            urls.AddRange(seasonMatches.Select(m => (
                $"/matchs/{m.Id}-{Kokora.Application.Common.Slug.From($"{m.Home} {m.Away}", 60)}", (DateTimeOffset?)m.Modified, "daily")));
            var players = await db.SquadMembers.AsNoTracking().Where(s => s.SeasonId == season.Id).Select(s => s.Player.Slug).Distinct().ToListAsync(ct);
            urls.AddRange(players.Select(p => ($"/joueurs/{p}", (DateTimeOffset?)null, "weekly")));
        }
        var clubs = await db.Clubs.AsNoTracking().Where(c => c.IsActive).Select(c => c.Slug).ToListAsync(ct);
        urls.AddRange(clubs.Select(c => ($"/equipes/{c}", (DateTimeOffset?)null, "weekly")));
        var articles = await NewsService.Live(db.Articles.AsNoTracking(), DateTimeOffset.UtcNow)
            .Select(a => new { a.Slug, Modified = a.UpdatedAt ?? a.PublishedAt }).ToListAsync(ct);
        urls.AddRange(articles.Select(a => ($"/infos/{a.Slug}", a.Modified, "monthly")));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "urlset", urls.DistinctBy(u => u.Path).Select(u => new XElement(ns + "url",
                new XElement(ns + "loc", BaseUrl + u.Path),
                u.Modified is { } m ? new XElement(ns + "lastmod", m.UtcDateTime.ToString("yyyy-MM-dd")) : null,
                new XElement(ns + "changefreq", u.Frequency)))));
        var sb = new StringBuilder();
        using (var writer = new Utf8StringWriter(sb)) doc.Save(writer);
        return sb.ToString();
    }

    private sealed class Utf8StringWriter(StringBuilder sb) : StringWriter(sb)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
