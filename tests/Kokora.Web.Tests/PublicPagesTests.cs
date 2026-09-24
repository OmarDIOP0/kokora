using System.Net;
using FluentAssertions;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

[Collection(DemoCollection.Name)]
public class PublicPagesTests(DemoDataFixture fx)
{
    [Fact]
    public async Task Public_pages_render_with_demo_data()
    {
        var ids = await fx.QueryAsync(async db => new
        {
            Played = await db.Matches.Where(m => m.IsDemo && m.Status == MatchStatus.Finished).Select(m => m.Id).FirstAsync(),
            Upcoming = await db.Matches.Where(m => m.IsDemo && m.Status == MatchStatus.Scheduled).Select(m => (int?)m.Id).FirstOrDefaultAsync(),
            FirstDay = await db.Matches.Where(m => m.IsDemo).MinAsync(m => m.KickoffAt),
        });
        var day = DateOnly.FromDateTime(Kokora.Application.Common.KokoraTime.ToLocal(ids.FirstDay!.Value).DateTime);

        var client = fx.Factory.ClientAs(null);
        string[] urls =
        [
            "/", "/?vue=a-venir", "/?vue=resultats", $"/?date={day:yyyy-MM-dd}", "/?c=zonale-5a", "/?c=toutes",
            "/classements/zonale-5a", "/classements/zonale-5b", "/classements/coupe-du-maire", "/classements/4-grandes-zone-5a",
            $"/matchs/{ids.Played}", "/plus", "/stats", "/equipes", "/equipes/asc-demo-1",
        ];
        foreach (var url in urls)
        {
            var res = await client.GetAsync(url);
            if (res.StatusCode == HttpStatusCode.MovedPermanently) res = await client.GetAsync(res.Headers.Location);
            var body = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} : {body[..Math.Min(body.Length, 1500)]}");
        }
    }

    [Fact]
    public async Task Day_view_shows_matches_grouped_by_competition()
    {
        var first = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo).OrderBy(m => m.KickoffAt).Select(m => m.KickoffAt).FirstAsync());
        var day = DateOnly.FromDateTime(Kokora.Application.Common.KokoraTime.ToLocal(first!.Value).DateTime);
        var html = await fx.Factory.ClientAs(null).GetStringAsync($"/?date={day:yyyy-MM-dd}&c=toutes");
        html.Should().Contain("Zonale 5A").And.Contain("ASC Démo").And.Contain("1re journée");
    }

    [Fact]
    public async Task Match_url_is_canonical_and_page_has_share_links()
    {
        var id = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo && m.Status == MatchStatus.Finished).Select(m => m.Id).FirstAsync());
        var client = fx.Factory.ClientAs(null);
        var res = await client.GetAsync($"/matchs/{id}");
        res.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        var canonical = res.Headers.Location!.ToString();
        canonical.Should().MatchRegex($"^/matchs/{id}-demo-\\d+-demo-\\d+$");

        var html = await client.GetStringAsync(canonical);
        html.Should().Contain("og:title").And.Contain("https://wa.me/?text=").And.Contain("Confrontations");
    }

    [Fact]
    public async Task Standings_page_lists_every_team_of_the_groups()
    {
        var html = await fx.Factory.ClientAs(null).GetStringAsync("/classements/zonale-5b");
        // Zone 5B : une poule de 5 et une poule de 4.
        foreach (var i in Enumerable.Range(9, 9)) html.Should().Contain($"ASC Démo {i}<");
        html.Should().Contain("Poule A").And.Contain("Poule B");
    }

    [Fact]
    public async Task Unknown_match_returns_a_french_404()
    {
        var res = await fx.Factory.ClientAs(null).GetAsync("/matchs/999999");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Page introuvable");
    }

    [Fact]
    public async Task Standings_match_the_demo_results()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var standings = scope.ServiceProvider.GetRequiredService<StandingsService>();
        var compId = await fx.QueryAsync(db => db.Competitions.Where(c => c.IsDemo && c.Slug == "zonale-5a").Select(c => c.Id).FirstAsync());
        var phases = await standings.CompetitionAsync(compId);
        var groups = phases.SelectMany(p => p.Groups).ToList();
        groups.Should().HaveCount(2);
        foreach (var g in groups)
        {
            var rows = g.Table.Rows;
            rows.Sum(r => r.GoalsFor).Should().Be(rows.Sum(r => r.GoalsAgainst));
            rows.Sum(r => r.Won).Should().Be(rows.Sum(r => r.Lost));
            rows.Select(r => r.Points).Should().BeInDescendingOrder();
            rows.Sum(r => r.Played).Should().Be(2 * g.PlayedMatches);
        }
    }
}
