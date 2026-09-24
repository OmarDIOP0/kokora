using Kokora.Application.Abstractions;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Tests;

/// <summary>
/// Outil de relecture visuelle (désactivé par défaut) : enregistre des pages admin rendues avec les données de démo
/// dans src/Kokora.Web/wwwroot/_snapshots pour les ouvrir dans un navigateur via le serveur de dev.
/// Activer avec la variable d'environnement KOKORA_SNAPSHOTS=1.
/// </summary>
[Collection(DemoCollection.Name)]
public class SnapshotTests(DemoDataFixture fx)
{
    [Fact]
    public async Task Write_admin_snapshots()
    {
        if (Environment.GetEnvironmentVariable("KOKORA_SNAPSHOTS") != "1") return;

        var ids = await fx.QueryAsync(async db => new
        {
            Competition = await db.Competitions.Where(c => c.IsDemo).OrderBy(c => c.Order).Select(c => c.Id).FirstAsync(),
            League = await db.Phases.Where(p => p.IsDemo && p.Type == PhaseType.League).Select(p => p.Id).FirstAsync(),
            Group = await db.Groups.Where(g => g.IsDemo).Select(g => g.Id).FirstAsync(),
            Club = await db.Clubs.Where(c => c.IsDemo).Select(c => c.Id).FirstAsync(),
            Match = await db.Matches.Where(m => m.IsDemo).Select(m => m.Id).FirstAsync(),
        });
        var pages = new Dictionary<string, string>
        {
            ["dashboard"] = "/admin", ["saisons"] = "/admin/saisons", ["competition"] = $"/admin/competitions/{ids.Competition}",
            ["competition-reglages"] = $"/admin/competitions/{ids.Competition}/modifier", ["phase"] = $"/admin/phases/{ids.League}",
            ["calendrier-poule"] = $"/admin/poules/{ids.Group}/calendrier", ["equipes"] = "/admin/equipes",
            ["equipe"] = $"/admin/equipes/{ids.Club}/modifier", ["joueurs"] = "/admin/joueurs", ["joueur-nouveau"] = "/admin/joueurs/nouveau",
            ["import"] = "/admin/joueurs/import", ["lieux"] = "/admin/lieux", ["matchs"] = "/admin/matchs",
            ["match"] = $"/admin/matchs/{ids.Match}/modifier", ["match-nouveau"] = "/admin/matchs/nouveau", ["demo"] = "/admin/demo",
            ["audit"] = "/admin/audit", ["discipline"] = "/admin/discipline",
        };

        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Kokora.Web/wwwroot/_snapshots"));
        Directory.CreateDirectory(dir);
        var client = fx.Factory.ClientAs(RolesForTests.Admin);
        foreach (var (name, url) in pages)
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), await client.GetStringAsync(url));

        // Pages publiques (visiteur anonyme).
        var yesterday = Kokora.Application.Common.KokoraTime.Today.AddDays(-1);
        var played = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo && m.Status == MatchStatus.Finished && m.HomePenalties != null)
            .Select(m => m.Id).FirstAsync());
        var publicPages = new Dictionary<string, string>
        {
            ["public-accueil"] = "/?c=toutes", ["public-hier"] = $"/?date={yesterday:yyyy-MM-dd}&c=toutes",
            ["public-resultats"] = "/?vue=resultats&c=toutes", ["public-a-venir"] = "/?vue=a-venir&c=toutes",
            ["public-classement-5b"] = "/classements/zonale-5b", ["public-tableau"] = "/classements/4-grandes-zone-5a",
            ["public-plus"] = "/plus", ["public-stats"] = "/stats?c=toutes", ["public-equipes"] = "/equipes",
            ["public-equipe"] = "/equipes/asc-demo-5", ["public-recherche"] = "/recherche?q=demo%201",
        };
        var anonymous = fx.Factory.CreateClient(); // suit les redirections (adresse canonique des matchs)
        foreach (var (name, url) in publicPages)
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), await anonymous.GetStringAsync(url));
        await File.WriteAllTextAsync(Path.Combine(dir, "public-match.html"), await anonymous.GetStringAsync($"/matchs/{played}"));
        var regular = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo && m.Status == MatchStatus.Finished && m.GroupId != null
            && m.Events.Count >= 3).Select(m => m.Id).FirstAsync());
        await File.WriteAllTextAsync(Path.Combine(dir, "public-match-poule.html"), await anonymous.GetStringAsync($"/matchs/{regular}"));
        var scorer = await fx.QueryAsync(db => db.MatchEvents.Where(e => e.IsDemo && e.Type == Kokora.Domain.Enums.MatchEventType.Goal)
            .GroupBy(e => e.Player!.Slug).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync());
        await File.WriteAllTextAsync(Path.Combine(dir, "public-joueur.html"), await anonymous.GetStringAsync($"/joueurs/{scorer}"));
    }
}
