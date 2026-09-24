using Kokora.Application.Abstractions;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        // Infos : couverture et galerie avec des images générées (écrites dans wwwroot/uploads, ignoré par git).
        var featured = await fx.QueryAsync(db => db.Articles.Where(a => a.IsDemo && a.IsFeatured).Select(a => new { a.Id, a.Slug }).FirstAsync());
        var draft = await fx.QueryAsync(db => db.Articles.Where(a => a.IsDemo && a.Status == ArticleStatus.Draft).Select(a => a.Id).FirstAsync());
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var images = scope.ServiceProvider.GetRequiredService<IImageStore>();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var article = await db.Articles.FirstAsync(a => a.Id == featured.Id);
            if (article.CoverImagePath is null)
            {
                article.CoverImagePath = (await images.SaveAsync(new MemoryStream(FakePhoto(1600, 900, 0)), "infos", "snapshot", ImagePresets.Cover))[0];
                await db.SaveChangesAsync();
                var photos = scope.ServiceProvider.GetRequiredService<Kokora.Application.Admin.PhotoAdminService>();
                var files = Enumerable.Range(1, 5).Select(i => ($"p{i}.png", (Func<Stream>)(() => new MemoryStream(FakePhoto(1200, 800, i))))).ToList();
                await photos.AddAsync(Kokora.Application.Admin.PhotoOwner.Article(featured.Id), files, "Démo");
                await photos.AddAsync(Kokora.Application.Admin.PhotoOwner.Match(played), files, null);
            }
        }
        var newsPages = new Dictionary<string, string>
        {
            ["public-infos"] = "/infos", ["public-infos-categorie"] = "/infos?categorie=commission", ["public-article"] = $"/infos/{featured.Slug}",
            ["public-match-photos"] = $"/matchs/{played}",
        };
        foreach (var (name, url) in newsPages)
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), await anonymous.GetStringAsync(url));
        var editor = fx.Factory.ClientAs(RolesForTests.Editor);
        var adminNews = new Dictionary<string, string>
        {
            ["infos"] = "/admin/infos", ["info-form"] = $"/admin/infos/{featured.Id}/modifier", ["info-brouillon"] = $"/admin/infos/{draft}/modifier",
            ["info-nouvelle"] = "/admin/infos/nouvelle", ["info-categories"] = "/admin/infos/categories", ["photos"] = "/admin/photos",
            ["photos-match"] = $"/admin/matchs/{played}/photos",
        };
        foreach (var (name, url) in adminNews)
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), await editor.GetStringAsync(url));

        // Direct : un match de démo programmé passe en 1re période avec un but et un carton.
        var liveId = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo && m.Status == MatchStatus.Scheduled && m.HomeClubId != null && m.AwayClubId != null)
            .OrderBy(m => m.KickoffAt).Select(m => new { m.Id, Home = m.HomeClubId!.Value, Away = m.AwayClubId!.Value }).FirstAsync());
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var live = scope.ServiceProvider.GetRequiredService<Kokora.Application.Live.LiveMatchService>();
            var state = await live.StateAsync(liveId.Id);
            if (state.Period == LivePeriod.NotStarted)
            {
                await live.AdvanceAsync(liveId.Id, LivePeriod.FirstHalf);
                await live.SetMinuteAsync(liveId.Id, 27);
                await live.AddEventAsync(liveId.Id, new() { Type = MatchEventType.Goal, ClubId = liveId.Home, PlayerId = state.HomeSquad[9].Id, AssistPlayerId = state.HomeSquad[7].Id });
                await live.AddEventAsync(liveId.Id, new() { Type = MatchEventType.YellowCard, ClubId = liveId.Away, PlayerId = state.AwaySquad[4].Id });
            }
        }
        await File.WriteAllTextAsync(Path.Combine(dir, "direct.html"), await client.GetStringAsync("/admin/direct"));
        await File.WriteAllTextAsync(Path.Combine(dir, "direct-match.html"), await client.GetStringAsync($"/admin/direct/{liveId.Id}"));
        await File.WriteAllTextAsync(Path.Combine(dir, "public-direct-accueil.html"), await anonymous.GetStringAsync("/?c=toutes&vue=a-venir"));
        await File.WriteAllTextAsync(Path.Combine(dir, "public-direct-match.html"), await anonymous.GetStringAsync($"/matchs/{liveId.Id}"));

        // Comptes : utilisateur simulé, pronostics, votes et commentaires de test.
        var final = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo && m.IsFeatured).Select(m => m.Id).FirstAsync());
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Kokora.Infrastructure.Identity.AppUser>>();
            if (await users.FindByIdAsync("test-user") is null)
                await users.CreateAsync(new Kokora.Infrastructure.Identity.AppUser { Id = "test-user", UserName = "testeur@kokora.test", Email = "testeur@kokora.test", DisplayName = "Awa (test)" });
            var predictions = scope.ServiceProvider.GetRequiredService<Kokora.Application.Engagement.PredictionService>();
            var votes = scope.ServiceProvider.GetRequiredService<Kokora.Application.Engagement.VoteService>();
            var comments = scope.ServiceProvider.GetRequiredService<Kokora.Application.Engagement.CommentService>();
            for (var i = 0; i < 6; i++)
                await predictions.SaveAsync(final, i == 0 ? "test-user" : $"fictif-{i}", i % 3, (i + 1) % 2);
            var voteBlock = await votes.BlockAsync(played, null);
            if (voteBlock is { Open: true, Candidates.Count: > 3 })
                for (var i = 0; i < 5; i++)
                    await votes.VoteAsync(played, $"fictif-{i}", voteBlock.Candidates[i % 3].PlayerId);
            var articleId = await fx.QueryAsync(db => db.Articles.Where(a => a.Slug == featured.Slug).Select(a => a.Id).FirstAsync());
            if (!await fx.QueryAsync(db => db.Comments.AnyAsync(c => c.ArticleId == articleId)))
            {
                await comments.PostAsync(articleId, "fictif-1", "Moussa (test)", "Commentaire de test publié.");
                foreach (var c in await comments.ModerationAsync(CommentStatus.Pending)) await comments.SetStatusAsync(c.Id, CommentStatus.Approved);
                await comments.PostAsync(articleId, "test-user", "Awa (test)", "Commentaire de test en attente de validation.");
            }
        }
        var user = fx.Factory.CreateClient(); // suit les redirections (adresse canonique des matchs)
        user.DefaultRequestHeaders.Add(KokoraWebFactory.RoleHeader, RolesForTests.User);
        var userPages = new Dictionary<string, string>
        {
            ["compte"] = "/compte", ["pronostics"] = "/pronostics", ["public-match-vote"] = $"/matchs/{played}",
            ["public-match-pronostic"] = $"/matchs/{final}", ["public-article-commentaires"] = $"/infos/{featured.Slug}", ["public-plus-connecte"] = "/plus",
        };
        foreach (var (name, url) in userPages)
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), await user.GetStringAsync(url));
        await File.WriteAllTextAsync(Path.Combine(dir, "inscription.html"), await anonymous.GetStringAsync("/compte/inscription"));
        foreach (var (name, url) in new Dictionary<string, string> { ["commentaires"] = "/admin/commentaires", ["utilisateurs"] = "/admin/utilisateurs", ["notifications"] = "/admin/notifications" })
            await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), await client.GetStringAsync(url));
    }

    /// <summary>Image de test : bandes de couleur, sans aucun contenu réel.</summary>
    private static byte[] FakePhoto(int w, int h, int seed)
    {
        using var bmp = new SkiaSharp.SKBitmap(w, h);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        SkiaSharp.SKColor[] palette = [new(0x0E, 0x6B, 0x3A), new(0x9A, 0x7A, 0x2E), new(0x26, 0x32, 0x38), new(0xC6, 0x28, 0x28), new(0x15, 0x65, 0xC0), new(0x55, 0x8B, 0x2F)];
        for (var i = 0; i < 6; i++)
            canvas.DrawRect(0, i * h / 6f, w, h / 6f + 1, new SkiaSharp.SKPaint { Color = palette[(i + seed) % palette.Length] });
        return bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90).ToArray();
    }
}
