using System.Net;
using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

[Collection(DemoCollection.Name)]
public class StatsAndDirectoryTests(DemoDataFixture fx)
{
    [Fact]
    public async Task Phase4_pages_render()
    {
        var player = await fx.QueryAsync(db => db.Players.Where(p => p.IsDemo).Select(p => p.Slug).FirstAsync());
        var anonymous = fx.Factory.ClientAs(null);
        foreach (var url in new[] { "/stats", "/stats?c=zonale-5a", "/stats?c=toutes", "/equipes", "/equipes/asc-demo-1", $"/joueurs/{player}",
                     "/recherche", "/recherche?q=demo", "/recherche?q=d%C3%A9mo%201" })
        {
            var res = await anonymous.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} : {body[..Math.Min(body.Length, 1500)]}");
        }
        (await anonymous.GetAsync("/equipes/inconnue")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fx.Factory.ClientAs(RolesForTests.Admin).GetAsync("/admin/discipline")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Scorer_totals_match_the_goals_scored()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var seasonId = await db.Seasons.Where(s => s.IsDemo).Select(s => s.Id).FirstAsync();
        var data = await scope.ServiceProvider.GetRequiredService<StatsService>().GetAsync(seasonId, null);

        var realGoals = await db.Matches.Where(m => m.IsDemo && StatsService.RealMatches.Contains(m.Status))
            .SumAsync(m => (m.HomeScore ?? 0) + (m.AwayScore ?? 0));
        data.Scorers.Sum(r => r.Goals).Should().Be(realGoals);
        data.Goals.Should().Be(realGoals);
        data.Scorers.Select(r => r.Goals).Should().BeInDescendingOrder();
        data.Teams.Sum(t => t.GoalsFor).Should().Be(data.Teams.Sum(t => t.GoalsAgainst));
        data.Contributions.Should().OnlyContain(r => r.Contributions == r.Goals + r.Assists);
    }

    [Fact]
    public async Task Search_ignores_accents_and_finds_teams_and_players()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<DirectoryService>();
        var accent = await directory.SearchAsync("Démo 12");
        var plain = await directory.SearchAsync("demo 12");
        accent.Teams.Select(t => t.Name).Should().Contain("ASC Démo 12");
        plain.Teams.Select(t => t.Id).Should().BeEquivalentTo(accent.Teams.Select(t => t.Id));
        plain.Players.Should().NotBeEmpty();
        (await directory.SearchAsync("x")).Teams.Should().BeEmpty();
    }

    [Fact]
    public async Task Team_page_follow_button_and_squad_are_present()
    {
        var html = await fx.Factory.ClientAs(null).GetStringAsync("/equipes/asc-demo-1");
        html.Should().Contain("$store.favs.toggle(").And.Contain("Joueur 9 Démo 1").And.Contain("Gardiens");
    }
}

public class DisciplineServiceTests(DisciplineFixture fx) : IClassFixture<DisciplineFixture>
{
    [Fact]
    public async Task Commission_decisions_update_suspensions_and_standings()
    {
        using var s = fx.Factory.Services.CreateScope();
        var sp = s.ServiceProvider;
        var db = sp.GetRequiredService<IAppDbContext>();
        var seasonId = await sp.GetRequiredService<SeasonAdminService>().SaveAsync(new SeasonInput { Year = 2090, Name = "Disc" });
        var comps = sp.GetRequiredService<CompetitionAdminService>();
        var compId = await comps.SaveAsync(new CompetitionInput { SeasonId = seasonId, Name = "Coupe Disc", ShortName = "CD" });
        var phase = await comps.SavePhaseAsync(new PhaseInput { CompetitionId = compId, Name = "Poules", Type = PhaseType.League });
        var clubA = await sp.GetRequiredService<ClubAdminService>().SaveAsync(new ClubInput { Name = "ASC Disc A", ShortName = "DA" }, null);
        var clubB = await sp.GetRequiredService<ClubAdminService>().SaveAsync(new ClubInput { Name = "ASC Disc B", ShortName = "DB" }, null);
        var groupId = await comps.SaveGroupAsync(new GroupInput { PhaseId = phase, Name = "Poule A", ClubIds = [clubA, clubB] });
        var playerId = await sp.GetRequiredService<PlayerAdminService>().SaveAsync(
            new PlayerInput { FirstName = "Test", LastName = "Discipline", ClubId = clubA }, seasonId, null);

        var discipline = sp.GetRequiredService<DisciplineAdminService>();
        await discipline.AddSuspensionAsync(new SuspensionInput { PlayerId = playerId, CompetitionId = compId, Matches = 2, Reason = "Test" }, seasonId);
        var stats = sp.GetRequiredService<StatsService>();
        var comp = await db.Competitions.AsNoTracking().SingleAsync(c => c.Id == compId);
        var (active, _) = await stats.DisciplineAsync([(comp.Id, comp.Name, comp.Suspensions)], CancellationToken.None);
        active.Should().ContainSingle(x => x.Player.Id == playerId && x.Remaining == 2);

        await discipline.AddAdjustmentAsync(new PointAdjustmentInput { GroupId = groupId, ClubId = clubB, Points = -3, Reason = "Test" });
        var table = (await sp.GetRequiredService<StandingsService>().CompetitionAsync(compId)).Single().Groups.Single().Table;
        table.Rows.Single(r => r.Team.Id == clubB).Points.Should().Be(-3);
        table.Rows[0].Team.Id.Should().Be(clubA);
    }
}
