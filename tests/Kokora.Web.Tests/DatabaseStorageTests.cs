using System.Net;
using FluentAssertions;
using Kokora.Application.Admin;
using Kokora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Kokora.Web.Tests;

/// <summary>Application configurée comme sur Render gratuit : aucun disque permanent (Storage:Mode = Database).</summary>
public class DatabaseStorageFixture : IDisposable
{
    public KokoraWebFactory Factory { get; } = new("kokora_tests_dbstorage");

    public DatabaseStorageFixture()
    {
        Factory.Settings["Storage:Mode"] = "Database";
        _ = Factory.Server;
    }

    public void Dispose() => Factory.Dispose();
}

public class DatabaseStorageTests(DatabaseStorageFixture fx) : IClassFixture<DatabaseStorageFixture>
{
    [Fact]
    public async Task Uploads_and_keys_live_in_the_database()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        using var bitmap = new SKBitmap(120, 120);
        bitmap.Erase(SKColors.DarkGreen);
        await using var logo = new MemoryStream(bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        var clubId = await scope.ServiceProvider.GetRequiredService<ClubAdminService>()
            .SaveAsync(new ClubInput { Name = "ASC Stockage", ShortName = "Stockage" }, logo);
        var url = (await db.Clubs.AsNoTracking().SingleAsync(c => c.Id == clubId)).LogoPath!;
        url.Should().StartWith("/uploads/logos/");

        var client = fx.Factory.ClientAs(null);
        var res = await client.GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        res.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromDays(365));

        // Clés VAPID en base, jamais servies ; clés de chiffrement des cookies en base.
        (await db.StoredFiles.AnyAsync(f => f.Key == "_system/vapid.json")).Should().BeTrue();
        (await client.GetAsync("/uploads/_system/vapid.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await client.GetAsync("/compte/connexion"); // génère un jeton anti-CSRF, donc une clé
        (await db.DataProtectionKeys.CountAsync()).Should().BeGreaterThan(0);

        // Suppression du logo : le fichier disparaît de la base.
        await scope.ServiceProvider.GetRequiredService<ClubAdminService>().DeleteAsync(clubId);
        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("postgresql://kokora:s3cr%40t@dpg-abc.frankfurt-postgres.render.com/kokora", "dpg-abc.frankfurt-postgres.render.com", "s3cr@t", "Require")]
    [InlineData("postgres://u:p@localhost:5433/db", "localhost", "p", "Prefer")]
    public void Hosting_urls_are_converted(string url, string host, string password, string ssl)
    {
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(Kokora.Infrastructure.DependencyInjection.ToNpgsql(url));
        (cs.Host, cs.Password, cs.SslMode.ToString()).Should().Be((host, password, ssl));
        Kokora.Infrastructure.DependencyInjection.ToNpgsql("Host=x;Database=y").Should().Be("Host=x;Database=y");
    }
}
