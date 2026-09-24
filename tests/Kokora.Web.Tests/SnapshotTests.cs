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
            ["audit"] = "/admin/audit",
        };

        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Kokora.Web/wwwroot/_snapshots"));
        Directory.CreateDirectory(dir);
        var client = fx.Factory.ClientAs(RolesForTests.Admin);
        foreach (var (name, url) in pages)
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), await client.GetStringAsync(url));
    }
}
