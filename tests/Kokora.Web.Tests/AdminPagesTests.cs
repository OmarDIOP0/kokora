using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

public class DemoDataFixture : IAsyncLifetime
{
    public KokoraWebFactory Factory { get; } = new();

    public async Task InitializeAsync()
    {
        _ = Factory.Server; // démarre l'application (migrations)
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataService>().SeedAsync();
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }

    public async Task<T> QueryAsync<T>(Func<IAppDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IAppDbContext>());
    }
}

/// <summary>Une seule base « démo » partagée par les classes qui en ont besoin (jamais recréée en parallèle).</summary>
[CollectionDefinition(Name)]
public class DemoCollection : ICollectionFixture<DemoDataFixture>
{
    public const string Name = "demo";
}

[Collection(DemoCollection.Name)]
public partial class AdminPagesTests(DemoDataFixture fx)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/design")]
    [InlineData("/design/accueil")]
    [InlineData("/compte/connexion")]
    public async Task Public_pages_render(string url)
    {
        var res = await fx.Factory.ClientAs(null).GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_requires_login()
    {
        var res = await fx.Factory.ClientAs(null).GetAsync("/admin");
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("/compte/connexion");
    }

    [Fact]
    public async Task Editor_sees_dashboard_but_not_sport_admin()
    {
        var client = fx.Factory.ClientAs(RolesForTests.Editor);
        (await client.GetAsync("/admin")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/admin/equipes")).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await client.GetAsync("/admin/audit")).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task All_admin_pages_render_with_demo_data()
    {
        var ids = await fx.QueryAsync(async db => new
        {
            Competition = await db.Competitions.Where(c => c.IsDemo).OrderBy(c => c.Order).Select(c => c.Id).FirstAsync(),
            League = await db.Phases.Where(p => p.IsDemo && p.Type == PhaseType.League).Select(p => p.Id).FirstAsync(),
            Knockout = await db.Phases.Where(p => p.IsDemo && p.Type == PhaseType.Knockout).Select(p => p.Id).FirstAsync(),
            Group = await db.Groups.Where(g => g.IsDemo).Select(g => g.Id).FirstAsync(),
            Club = await db.Clubs.Where(c => c.IsDemo).Select(c => c.Id).FirstAsync(),
            Player = await db.Players.Where(p => p.IsDemo).Select(p => p.Id).FirstAsync(),
            Match = await db.Matches.Where(m => m.IsDemo).Select(m => m.Id).FirstAsync(),
            Season = await db.Seasons.Where(s => s.IsDemo).Select(s => s.Id).FirstAsync(),
        });

        var client = fx.Factory.ClientAs(RolesForTests.Admin);
        string[] urls =
        [
            "/admin", "/admin/saisons", "/admin/saisons/nouvelle", $"/admin/saisons/{ids.Season}/modifier",
            $"/admin/competitions/{ids.Competition}", $"/admin/competitions/{ids.Competition}/modifier",
            $"/admin/saisons/{ids.Season}/competitions/nouvelle", $"/admin/competitions/{ids.Competition}/phases/nouvelle",
            $"/admin/phases/{ids.League}", $"/admin/phases/{ids.Knockout}", $"/admin/phases/{ids.League}/modifier",
            $"/admin/poules/{ids.Group}/modifier", $"/admin/poules/{ids.Group}/calendrier",
            "/admin/equipes", "/admin/equipes?q=demo&zone=5A", "/admin/equipes/nouvelle", $"/admin/equipes/{ids.Club}/modifier",
            "/admin/joueurs", $"/admin/joueurs?equipe={ids.Club}", "/admin/joueurs?q=joueur", "/admin/joueurs/nouveau",
            $"/admin/joueurs/{ids.Player}/modifier", "/admin/joueurs/import", "/admin/joueurs/modele.csv",
            "/admin/lieux", "/admin/matchs", "/admin/matchs?sansResultat=true", $"/admin/matchs?equipe={ids.Club}",
            "/admin/matchs/nouveau", $"/admin/matchs/{ids.Match}/modifier", "/admin/demo", "/admin/audit",
        ];
        foreach (var url in urls)
        {
            var res = await client.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} doit s'afficher. Réponse : {body[..Math.Min(body.Length, 2000)]}");
        }
    }

    [Fact]
    public async Task Infinite_scroll_returns_next_rows_only()
    {
        var client = fx.Factory.ClientAs(RolesForTests.Admin);
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/joueurs?page=2");
        req.Headers.Add("HX-Request", "true");
        var res = await client.SendAsync(req);
        var html = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().NotContain("<html").And.Contain("/admin/joueurs/");
    }

    [Fact]
    public async Task Creating_a_season_through_the_form_works_with_antiforgery()
    {
        var client = fx.Factory.ClientAs(RolesForTests.Admin);
        var form = await client.GetStringAsync("/admin/saisons/nouvelle");
        var token = TokenRegex().Match(form).Groups[1].Value;
        token.Should().NotBeEmpty();

        var res = await client.PostAsync("/admin/saisons/enregistrer", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Year"] = "2031", ["Name"] = "Saison test 2031", ["IsCurrent"] = "false", ["__RequestVerificationToken"] = token
        }));
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await fx.QueryAsync(db => db.Seasons.AnyAsync(s => s.Year == 2031))).Should().BeTrue();

        // Sans jeton : refusé (retour au formulaire avec un message, rien n'est enregistré).
        var forged = await client.PostAsync("/admin/saisons/enregistrer", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Year"] = "2032", ["Name"] = "Pirate"
        }));
        forged.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await fx.QueryAsync(db => db.Seasons.AnyAsync(s => s.Year == 2032))).Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_form_is_redisplayed_with_french_errors()
    {
        var client = fx.Factory.ClientAs(RolesForTests.Admin);
        var form = await client.GetStringAsync("/admin/equipes/nouvelle");
        var token = TokenRegex().Match(form).Groups[1].Value;
        var res = await client.PostAsync("/admin/equipes/enregistrer", new MultipartFormDataContent
        {
            { new StringContent(""), "Input.Name" },
            { new StringContent("#123"), "Input.PrimaryColor" },
            { new StringContent(token), "__RequestVerificationToken" }
        });
        var html = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK, html[..Math.Min(html.Length, 4000)]);
        html.Should().Contain("Le nom est obligatoire.").And.Contain("Couleur au format #RRGGBB.");
    }

    [GeneratedRegex("__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}
