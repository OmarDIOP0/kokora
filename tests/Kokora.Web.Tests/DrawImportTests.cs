using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

public class DrawFixture() : ServicesFixture("kokora_tests_draw");

public class DrawImportTests(DrawFixture fx) : IClassFixture<DrawFixture>
{
    [Fact]
    public async Task Official_draw_is_imported_then_filled_with_removable_fake_data()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IAppDbContext>();
        var seasons = sp.GetRequiredService<SeasonAdminService>();
        var seasonId = await seasons.SaveAsync(new SeasonInput { Year = 2095, Name = "Saison 2094-2095" });
        await seasons.CreateStandardStructureAsync(seasonId);

        const string draw = """
            TIRAGE ZONE 5A
            Poule A : THIOSSANE, DIAMONO, PIKINE, NATANGUÉ, MBAXAAL
            Poule B : Médine, Avenir, Génération, Diambar, Manko
            Zone 5B
            • Poule A : Diamaguène, Daradji, Téranga, Espoir
            """;
        var imports = sp.GetRequiredService<DrawImportService>();
        var report = await imports.ImportAsync(seasonId, draw);
        report.Should().Match<DrawReport>(r => r.Groups == 3 && r.ClubsCreated == 14);

        var thiossane = await db.Clubs.SingleAsync(c => c.Slug == "asc-thiossane");
        (thiossane.Name, thiossane.ShortName, thiossane.Zone).Should().Be(("ASC Thiossane", "Thiossane", "5A"));
        (await db.Clubs.SingleAsync(c => c.ShortName == "Diamaguène")).Zone.Should().Be("5B");

        // Réimport : rien n'est dupliqué.
        (await imports.ImportAsync(seasonId, draw)).ClubsCreated.Should().Be(0);
        (await db.Groups.CountAsync(g => g.Phase.Competition.SeasonId == seasonId)).Should().Be(3);

        var bad = () => imports.ImportAsync(seasonId, "Poule A : X, Y");
        await bad.Should().ThrowAsync<BusinessRuleException>();

        var fill = await sp.GetRequiredService<DemoDataService>().FillSeasonAsync(seasonId);
        fill.Players.Should().Be(14 * 14);
        fill.Matches.Should().Be(10 + 10 + 6);
        fill.Played.Should().BeGreaterThan(0);

        // Votes fictifs : un homme du match désigné pour chaque match joué dont le vote est clos (48 h).
        var stats = await sp.GetRequiredService<Kokora.Application.Public.StatsService>().GetAsync(seasonId, null);
        var closed = await db.Matches.CountAsync(m => m.Phase.Competition.SeasonId == seasonId && m.Status == Kokora.Domain.Enums.MatchStatus.Finished
            && m.KickoffAt < DateTimeOffset.UtcNow.AddHours(-50));
        closed.Should().BeGreaterThan(0);
        stats.MenOfTheMatch!.Sum(r => r.ManOfTheMatch).Should().Be(closed);
        stats.MenOfTheMatch!.Select(r => r.ManOfTheMatch).Should().BeInDescendingOrder();

        // Suppression du fictif : équipes et poules réelles conservées.
        await sp.GetRequiredService<DemoDataService>().PurgeAsync();
        (await db.Matches.CountAsync(m => m.Phase.Competition.SeasonId == seasonId && m.GroupId != null)).Should().Be(0); // tableaux vides des 4 Grandes conservés
        (await db.Players.CountAsync(p => p.LastName == "Thiossane")).Should().Be(0);
        (await db.Clubs.CountAsync(c => c.Zone == "5A")).Should().Be(10);
        (await db.GroupTeams.CountAsync(t => t.Group.Phase.Competition.SeasonId == seasonId)).Should().Be(14);
    }
}
