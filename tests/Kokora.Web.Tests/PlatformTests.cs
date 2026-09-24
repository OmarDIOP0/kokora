using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kokora.Infrastructure.Analytics;
using Kokora.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

public class VisitSectionTests
{
    [Theory]
    [InlineData("/", "matchs")]
    [InlineData("/matchs/12-demo-1-demo-2", "match")]
    [InlineData("/classements/zonale-5a", "classements")]
    [InlineData("/infos/une-info", "infos")]
    [InlineData("/admin", null)]
    [InlineData("/compte", null)]
    [InlineData("/sitemap.xml", null)]
    public void Sections_are_derived_from_the_path(string path, string? expected) =>
        VisitCounting.Section(new PathString(path)).Should().Be(expected);
}

[Collection(DemoCollection.Name)]
public class PlatformTests(DemoDataFixture fx)
{
    [Fact]
    public async Task Security_headers_are_sent_and_inline_scripts_carry_the_nonce()
    {
        var res = await fx.Factory.ClientAs(null).GetAsync("/");
        var csp = res.Headers.GetValues("Content-Security-Policy").Single();
        csp.Should().Contain("default-src 'self'").And.Contain("frame-ancestors 'none'").And.Contain("object-src 'none'");
        res.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        res.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        res.Headers.GetValues("Referrer-Policy").Should().ContainSingle();

        var nonce = Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;
        nonce.Should().NotBeEmpty();
        var html = await res.Content.ReadAsStringAsync();
        // Tous les scripts en ligne exécutables portent le nonce de la requête ; aucun gestionnaire onclick/onchange.
        Regex.Matches(html, "<script(?![^>]*\\bsrc=)(?![^>]*type=\"application/(?:ld\\+)?json\")[^>]*>").Should()
            .OnlyContain(m => m.Value.Contains($"nonce=\"{nonce}\""));
        html.Should().NotMatchRegex(" on(click|change|submit|load)=\"");
    }

    [Fact]
    public async Task Admin_pages_have_no_inline_handlers()
    {
        var html = await fx.Factory.ClientAs(RolesForTests.Admin).GetStringAsync("/admin/utilisateurs");
        html.Should().NotMatchRegex(" on(click|change|submit|load)=\"").And.Contain("data-autosubmit");
    }

    [Fact]
    public async Task Seo_files_are_served()
    {
        var client = fx.Factory.ClientAs(null);
        var robots = await client.GetStringAsync("/robots.txt");
        robots.Should().Contain("Disallow: /admin").And.Contain("Sitemap: http://localhost/sitemap.xml");

        var sitemap = await client.GetAsync("/sitemap.xml");
        sitemap.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        var xml = await sitemap.Content.ReadAsStringAsync();
        xml.Should().Contain("<loc>http://localhost/equipes/asc-demo-1</loc>").And.Contain("/matchs/").And.Contain("/infos/");
        xml.Should().NotContain("brouillon"); // les infos non publiées n'y figurent pas

        var match = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo).Select(m => m.Id).FirstAsync());
        var page = await fx.Factory.CreateClient().GetStringAsync($"/matchs/{match}");
        page.Should().Contain("application/ld+json");
        page.Should().Contain("\"@type\":\"SportsEvent\"");
        page.Should().Contain("og:image");
        var home = await client.GetStringAsync("/");
        home.Should().Contain("rel=\"manifest\"").And.Contain("/icons/og-default.png");
    }

    [Fact]
    public async Task Pwa_resources_and_health_check()
    {
        var client = fx.Factory.ClientAs(null);
        var manifest = await client.GetAsync("/manifest.webmanifest");
        manifest.StatusCode.Should().Be(HttpStatusCode.OK);
        (await manifest.Content.ReadAsStringAsync()).Should().Contain("\"display\": \"standalone\"");
        manifest.Headers.CacheControl!.NoCache.Should().BeTrue();
        (await client.GetAsync("/hors-ligne")).StatusCode.Should().Be(HttpStatusCode.OK);
        var health = await client.GetAsync("/sante");
        health.StatusCode.Should().Be(HttpStatusCode.OK);
        (await health.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Page_views_are_counted_anonymously()
    {
        var recorder = fx.Factory.Services.GetRequiredService<VisitRecorder>();
        await recorder.FlushAsync(CancellationToken.None);
        int Views(string section) => fx.QueryAsync(db => db.DailyVisits.Where(v => v.Section == section).SumAsync(v => v.Count)).GetAwaiter().GetResult();
        var before = Views("classements");

        var client = fx.Factory.ClientAs(null);
        var first = await client.GetAsync("/classements/zonale-5a");
        await client.GetAsync("/classements/zonale-5a");
        var bot = new HttpRequestMessage(HttpMethod.Get, "/classements/zonale-5a");
        bot.Headers.UserAgent.ParseAdd("Googlebot/2.1");
        await client.SendAsync(bot);
        var htmx = new HttpRequestMessage(HttpMethod.Get, "/classements/zonale-5a");
        htmx.Headers.Add("HX-Request", "true");
        await client.SendAsync(htmx);

        await recorder.FlushAsync(CancellationToken.None);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        Views("classements").Should().Be(before + 2); // robot et fragment htmx ignorés
    }
}
