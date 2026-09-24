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

        // Homme du match fictif désigné pour chaque match joué ; les stats comptent ces désignations.
        var stats = await sp.GetRequiredService<Kokora.Application.Public.StatsService>().GetAsync(seasonId, null);
        var finished = await db.Matches.CountAsync(m => m.Phase.Competition.SeasonId == seasonId && m.Status == Kokora.Domain.Enums.MatchStatus.Finished);
        stats.MenOfTheMatch!.Sum(r => r.ManOfTheMatch).Should().Be(finished);
        stats.MenOfTheMatch!.Select(r => r.ManOfTheMatch).Should().BeInDescendingOrder();

        // Suppression du fictif : équipes et poules réelles conservées.
        await sp.GetRequiredService<DemoDataService>().PurgeAsync();
        (await db.Matches.CountAsync(m => m.Phase.Competition.SeasonId == seasonId && m.GroupId != null)).Should().Be(0); // tableaux vides des 4 Grandes conservés
        (await db.Players.CountAsync(p => p.LastName == "Thiossane")).Should().Be(0);
        (await db.Clubs.CountAsync(c => c.Zone == "5A")).Should().Be(10);
        (await db.GroupTeams.CountAsync(t => t.Group.Phase.Competition.SeasonId == seasonId)).Should().Be(14);
    }

    [Fact]
    public async Task Quick_entry_saves_scores_forfeits_and_man_of_the_match()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IAppDbContext>();
        var seasons = sp.GetRequiredService<SeasonAdminService>();
        var seasonId = await seasons.SaveAsync(new SeasonInput { Year = 2096, Name = "Saison rapide" });
        await seasons.CreateStandardStructureAsync(seasonId);
        await sp.GetRequiredService<DrawImportService>().ImportAsync(seasonId, "Zone 5A\nPoule A : Rapide 1, Rapide 2, Rapide 3, Rapide 4");
        var fill = await sp.GetRequiredService<DemoDataService>().FillSeasonAsync(seasonId);
        // On remet les matchs passés « à saisir » pour simuler des résultats non encore enregistrés.
        await db.Matches.Where(m => m.Phase.Competition.SeasonId == seasonId && m.GroupId != null).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.Status, Kokora.Domain.Enums.MatchStatus.Scheduled).SetProperty(m => m.HomeScore, (int?)null)
            .SetProperty(m => m.AwayScore, (int?)null).SetProperty(m => m.ManOfTheMatchPlayerId, (int?)null));
        await db.MatchEvents.Where(e => e.Match.Phase.Competition.SeasonId == seasonId).ExecuteDeleteAsync();

        // Nouvelle requête (comme dans l'application) : aucune entité périmée en mémoire.
        using var request = fx.Factory.Services.CreateScope();
        sp = request.ServiceProvider;
        db = sp.GetRequiredService<IAppDbContext>();
        var quick = sp.GetRequiredService<QuickResultService>();
        var pending = await quick.PendingAsync(seasonId, null);
        pending.Count.Should().Be(fill.Played);
        var (a, b, c) = (pending[0], pending[1], pending[2]);
        var report = await quick.SaveAsync(
        [
            new QuickResultRow { MatchId = a.Id, HomeScore = 2, AwayScore = 1, ManOfTheMatchPlayerId = a.HomeSquad[8].Id },
            new QuickResultRow { MatchId = b.Id, Forfeit = "away" },
            new QuickResultRow { MatchId = c.Id, HomeScore = 1, AwayScore = 1, ManOfTheMatchPlayerId = pending[3].HomeSquad[0].Id }, // joueur d'un autre match
            new QuickResultRow { MatchId = pending[3].Id } // ligne vide : ignorée
        ]);
        report.Saved.Should().Be(2);
        report.Errors.Keys.Should().Equal(c.Id);

        var saved = await db.Matches.AsNoTracking().SingleAsync(m => m.Id == a.Id);
        (saved.Status, saved.HomeScore, saved.AwayScore, saved.ManOfTheMatchPlayerId).Should().Be((Kokora.Domain.Enums.MatchStatus.Finished, 2, 1, a.HomeSquad[8].Id));
        (await db.Matches.AsNoTracking().SingleAsync(m => m.Id == b.Id)).Status.Should().Be(Kokora.Domain.Enums.MatchStatus.Forfeit);

        // Deuxième onglet : désigner après coup.
        (await quick.WithoutManOfTheMatchAsync(seasonId, null)).Select(m => m.Id).Should().NotContain(a.Id);
        var later = await quick.SaveManOfTheMatchAsync([new QuickResultRow { MatchId = a.Id, ManOfTheMatchPlayerId = a.AwaySquad[3].Id }]);
        later.Saved.Should().Be(1);
        var stats = await sp.GetRequiredService<Kokora.Application.Public.StatsService>().GetAsync(seasonId, null);
        stats.MenOfTheMatch!.Should().ContainSingle(r => r.Player.Id == a.AwaySquad[3].Id && r.ManOfTheMatch == 1);
    }
}
