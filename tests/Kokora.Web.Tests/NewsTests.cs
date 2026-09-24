using System.Net;
using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Kokora.Infrastructure.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Kokora.Web.Tests;

public class HtmlCleanerTests
{
    private readonly HtmlCleaner _cleaner = new();

    [Fact]
    public void Removes_scripts_styles_and_event_handlers()
    {
        var html = _cleaner.Clean("<p style=\"color:red\" onclick=\"x()\">Bonjour <script>alert(1)</script><strong>ici</strong></p>" +
                                  "<iframe src=\"https://exemple.sn\"></iframe><img src=\"x\" onerror=\"alert(1)\">");
        html.Should().Be("<p>Bonjour <strong>ici</strong></p>");
    }

    [Fact]
    public void Keeps_allowed_formatting_and_secures_links()
    {
        var html = _cleaner.Clean("<h2>Titre</h2><ul><li>Un</li></ul><blockquote>Cité</blockquote>" +
                                  "<p><a href=\"https://odcav.sn\">lien</a> <a href=\"javascript:alert(1)\">piège</a></p>");
        html.Should().Contain("<h2>Titre</h2>").And.Contain("<ul><li>Un</li></ul>").And.Contain("<blockquote>Cité</blockquote>");
        html.Should().Contain("href=\"https://odcav.sn\"").And.Contain("target=\"_blank\"").And.Contain("rel=\"noopener nofollow\"");
        html.Should().NotContain("javascript");
    }

    [Fact]
    public void Keeps_local_images_only_over_https_or_uploads()
    {
        _cleaner.Clean("<p><img src=\"/uploads/infos/a.webp\"></p>").Should().Contain("src=\"/uploads/infos/a.webp\"").And.Contain("loading=\"lazy\"");
        _cleaner.Clean("<p><img src=\"http://exemple.sn/a.jpg\">x</p>").Should().Be("<p>x</p>");
    }

    [Fact]
    public void Unwraps_unknown_tags_and_drops_empty_paragraphs()
    {
        _cleaner.Clean("<div><span>Texte&nbsp;collé</span></div><p><br></p>").Should().Be("Texte collé");
        _cleaner.ToPlainText("<h2>Titre</h2><p>Un &amp; deux</p>").Should().Be("Titre Un & deux");
    }
}

public class NewsFixture() : ServicesFixture("kokora_tests_news");

public class ArticleServiceTests(NewsFixture fx) : IClassFixture<NewsFixture>
{
    private (T, IAppDbContext, IServiceScope) Get<T>() where T : notnull
    {
        var scope = fx.Factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<T>(), scope.ServiceProvider.GetRequiredService<IAppDbContext>(), scope);
    }

    [Fact]
    public async Task Default_categories_are_created_at_startup()
    {
        var (articles, _, scope) = Get<ArticleAdminService>();
        using var _s = scope;
        (await articles.CategoriesAsync()).Select(c => c.Name).Should().Contain(["Communiqués", "Commission"]);
    }

    [Fact]
    public async Task Publication_modes_control_visibility()
    {
        var (articles, db, scope) = Get<ArticleAdminService>();
        using var _s = scope;
        var news = scope.ServiceProvider.GetRequiredService<NewsService>();

        var draftId = await articles.SaveAsync(new ArticleInput { Title = "Brouillon test", Body = "<p>Texte</p>" }, null);
        var scheduledId = await articles.SaveAsync(new ArticleInput
        {
            Title = "Programmée test", Body = "<p>Texte</p>", Mode = PublishMode.Scheduled,
            PublishAtLocal = KokoraTime.Now.DateTime.AddDays(1)
        }, null);
        var liveId = await articles.SaveAsync(new ArticleInput
        {
            Title = "Publiée test", Body = "<p>Texte <script>x</script></p>", Mode = PublishMode.Now, Tags = ["Finale", "finale"]
        }, null);

        var draft = await db.Articles.AsNoTracking().SingleAsync(a => a.Id == draftId);
        (await news.ArticleAsync(draft.Slug, preview: false)).Should().BeNull();
        (await news.ArticleAsync(draft.Slug, preview: true)).Should().NotBeNull();
        var scheduled = await db.Articles.AsNoTracking().SingleAsync(a => a.Id == scheduledId);
        (await news.ArticleAsync(scheduled.Slug, preview: false)).Should().BeNull();

        var live = await db.Articles.AsNoTracking().Include(a => a.Tags).SingleAsync(a => a.Id == liveId);
        live.Body.Should().Be("<p>Texte </p>");
        live.Tags.Should().ContainSingle(t => t.Slug == "finale"); // doublon ignoré
        var list = await news.ListAsync(new NewsFilter(), 1);
        list.Items.Select(i => i.Id).Should().Contain(liveId).And.NotContain([draftId, scheduledId]);

        // L'heure passée, l'info programmée apparaît sans action.
        await db.Articles.Where(a => a.Id == scheduledId).ExecuteUpdateAsync(s => s.SetProperty(a => a.PublishedAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        (await news.ArticleAsync(scheduled.Slug, preview: false)).Should().NotBeNull();
    }

    [Fact]
    public async Task Invalid_publications_are_refused()
    {
        var (articles, _, scope) = Get<ArticleAdminService>();
        using var _s = scope;
        var past = () => articles.SaveAsync(new ArticleInput
        {
            Title = "Passé", Body = "<p>x</p>", Mode = PublishMode.Scheduled, PublishAtLocal = KokoraTime.Now.DateTime.AddHours(-2)
        }, null);
        (await past.Should().ThrowAsync<BusinessRuleException>()).Which.Field.Should().Be("PublishAtLocal");

        var empty = () => articles.SaveAsync(new ArticleInput { Title = "Vide", Body = "<p><br></p>", Mode = PublishMode.Now }, null);
        (await empty.Should().ThrowAsync<BusinessRuleException>()).Which.Field.Should().Be("Body");
    }

    [Fact]
    public async Task Slugs_are_unique_and_editable()
    {
        var (articles, db, scope) = Get<ArticleAdminService>();
        using var _s = scope;
        var a = await articles.SaveAsync(new ArticleInput { Title = "Même titre" }, null);
        var b = await articles.SaveAsync(new ArticleInput { Title = "Même titre" }, null);
        var slugs = await db.Articles.Where(x => x.Id == a || x.Id == b).Select(x => x.Slug).ToListAsync();
        slugs.Should().BeEquivalentTo(["meme-titre", "meme-titre-2"]);

        var clash = () => articles.SaveAsync(new ArticleInput { Id = b, Title = "Même titre", Slug = "meme-titre" }, null);
        (await clash.Should().ThrowAsync<BusinessRuleException>()).Which.Field.Should().Be("Slug");
    }

    [Fact]
    public async Task Photos_are_added_one_by_one_and_invalid_files_reported()
    {
        var (photos, db, scope) = Get<PhotoAdminService>();
        using var _s = scope;
        var articleId = await scope.ServiceProvider.GetRequiredService<ArticleAdminService>()
            .SaveAsync(new ArticleInput { Title = "Galerie test" }, null);

        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(SKColors.SeaGreen);
        var png = bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        var result = await photos.AddAsync(PhotoOwner.Article(articleId),
        [
            ("vraie.png", () => new MemoryStream(png)),
            ("fausse.jpg", () => new MemoryStream("pas une image"u8.ToArray()))
        ], "Club photo");

        result.Added.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.StartsWith("fausse.jpg"));
        var list = await photos.ListAsync(PhotoOwner.Article(articleId));
        list.Should().ContainSingle(p => p.Width == 64 && p.Height == 48 && p.Credit == "Club photo");

        // Nettoyage des fichiers écrits dans wwwroot/uploads.
        await photos.DeleteAsync(list[0].Id);
        (await db.Photos.AnyAsync(p => p.ArticleId == articleId)).Should().BeFalse();
    }
}

[Collection(DemoCollection.Name)]
public class NewsPagesTests(DemoDataFixture fx)
{
    [Fact]
    public async Task Public_pages_render_and_hide_unpublished()
    {
        var slugs = await fx.QueryAsync(db => db.Articles.Where(a => a.IsDemo)
            .Select(a => new { a.Slug, a.Status }).ToListAsync());
        var published = slugs.First(s => s.Status == ArticleStatus.Published).Slug;
        var draft = slugs.First(s => s.Status == ArticleStatus.Draft).Slug;

        var anonymous = fx.Factory.ClientAs(null);
        foreach (var url in new[] { "/infos", "/infos?categorie=communiques", "/infos?tag=finale-demo", "/infos?equipe=asc-demo-1",
                     "/infos?page=2", $"/infos/{published}" })
        {
            var res = await anonymous.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} : {body[..Math.Min(body.Length, 3000)]}");
        }
        (await anonymous.GetAsync($"/infos/{draft}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/infos?tag=inconnu")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var preview = await fx.Factory.ClientAs(RolesForTests.Editor).GetStringAsync($"/infos/{draft}");
        preview.Should().Contain("Aperçu réservé à la rédaction");

        var list = await anonymous.GetStringAsync("/infos");
        list.Should().Contain("À la une").And.NotContain("brouillon démo");
        var team = await anonymous.GetStringAsync("/equipes/asc-demo-1");
        team.Should().Contain("Finale des 4 Grandes Zone 5A");
    }

    [Fact]
    public async Task Editors_manage_content_but_not_competitions()
    {
        var ids = await fx.QueryAsync(async db => new
        {
            Article = await db.Articles.Where(a => a.IsDemo).Select(a => a.Id).FirstAsync(),
            Match = await db.Matches.Where(m => m.IsDemo).Select(m => m.Id).FirstAsync()
        });
        var editor = fx.Factory.ClientAs(RolesForTests.Editor);
        foreach (var url in new[] { "/admin/infos", "/admin/infos?statut=brouillons", "/admin/infos/nouvelle", $"/admin/infos/{ids.Article}/modifier",
                     "/admin/infos/categories", "/admin/photos", $"/admin/matchs/{ids.Match}/photos" })
        {
            var res = await editor.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} : {body[..Math.Min(body.Length, 3000)]}");
        }
        var nav = await editor.GetStringAsync("/admin/infos");
        nav.Should().NotContain("href=\"/admin/equipes\"");
        (await editor.GetAsync("/admin/equipes")).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Match_page_lists_related_news()
    {
        var url = await fx.QueryAsync(db => db.Articles.Where(a => a.IsDemo && a.Matches.Any())
            .SelectMany(a => a.Matches).Select(m => m.Id).FirstAsync());
        var html = await fx.Factory.CreateClient().GetStringAsync($"/matchs/{url}");
        html.Should().Contain("Infos sur ce match");
    }
}
