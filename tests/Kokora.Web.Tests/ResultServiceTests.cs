using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

public partial class ResultServiceTests(ResultsFixture fx) : IClassFixture<ResultsFixture>
{
    private static int _year = 2060;
    private T Get<T>(IServiceScope s) where T : notnull => s.ServiceProvider.GetRequiredService<T>();

    /// <summary>Une poule de 4 (matchs générés) et un tableau à 4 (demies + finale).</summary>
    private async Task<(int CompId, int GroupId, int KnockoutPhaseId, List<int> Clubs)> SetupAsync(string tag)
    {
        using var s = fx.Factory.Services.CreateScope();
        var seasonId = await Get<SeasonAdminService>(s).SaveAsync(new SeasonInput { Year = Interlocked.Increment(ref _year), Name = $"R {tag}" });
        var comps = Get<CompetitionAdminService>(s);
        var compId = await comps.SaveAsync(new CompetitionInput { SeasonId = seasonId, Name = $"Coupe {tag}", ShortName = tag });
        var pools = await comps.SavePhaseAsync(new PhaseInput { CompetitionId = compId, Name = "Poules", Type = PhaseType.League });
        var ko = await comps.SavePhaseAsync(new PhaseInput { CompetitionId = compId, Name = "Finale", Type = PhaseType.Knockout, HasPenalties = true });
        var clubs = new List<int>();
        for (var i = 1; i <= 4; i++)
            clubs.Add(await Get<ClubAdminService>(s).SaveAsync(new ClubInput { Name = $"ASC R {tag} {i}", ShortName = $"{tag}{i}" }, null));
        var groupId = await comps.SaveGroupAsync(new GroupInput { PhaseId = pools, Name = "Poule A", ClubIds = clubs, QualifiedCount = 2 });
        await Get<ScheduleService>(s).GenerateGroupFixturesAsync(new FixtureGenerationInput
        {
            GroupId = groupId, FirstDate = new DateOnly(2040, 7, 1), DaysBetweenMatchdays = 2, FirstKickoff = new TimeOnly(16, 0), MinutesBetweenMatches = 120
        });
        await comps.CreateKnockoutBracketAsync(new KnockoutInput { PhaseId = ko, TeamCount = 4 });
        return (compId, groupId, ko, clubs);
    }

    private static ResultInput Score(int matchId, int h, int a, int? hp = null, int? ap = null) =>
        new() { MatchId = matchId, Status = MatchStatus.Finished, HomeScore = h, AwayScore = a, HomePenalties = hp, AwayPenalties = ap };

    [Fact]
    public async Task Saving_a_result_updates_the_cached_standings()
    {
        var (compId, groupId, _, _) = await SetupAsync("std");
        using var s = fx.Factory.Services.CreateScope();
        var standings = Get<StandingsService>(s);
        var before = await standings.CompetitionAsync(compId);
        before.SelectMany(p => p.Groups).Single().Table.Rows.Should().OnlyContain(r => r.Points == 0);

        var match = await Get<IAppDbContext>(s).Matches.OrderBy(m => m.KickoffAt).FirstAsync(m => m.GroupId == groupId);
        await Get<ResultService>(s).SaveAsync(Score(match.Id, 2, 1));

        var after = (await standings.CompetitionAsync(compId)).SelectMany(p => p.Groups).Single();
        after.PlayedMatches.Should().Be(1);
        after.Table.Rows[0].Team.Id.Should().Be(match.HomeClubId!.Value);
        after.Table.Rows[0].Points.Should().Be(3);
    }

    [Fact]
    public async Task Forfeit_applies_the_administrative_score()
    {
        var (_, groupId, _, _) = await SetupAsync("ff");
        using var s = fx.Factory.Services.CreateScope();
        var db = Get<IAppDbContext>(s);
        var match = await db.Matches.FirstAsync(m => m.GroupId == groupId);
        await Get<ResultService>(s).SaveAsync(new ResultInput
        {
            MatchId = match.Id, Status = MatchStatus.Forfeit, ForfeitingClubId = match.AwayClubId, HomeScore = 0, AwayScore = 5
        });
        var saved = await db.Matches.AsNoTracking().SingleAsync(m => m.Id == match.Id);
        (saved.HomeScore, saved.AwayScore).Should().Be((3, 0));
    }

    [Fact]
    public async Task Knockout_draw_requires_penalties_and_the_winner_advances()
    {
        var (_, _, ko, clubs) = await SetupAsync("ko");
        using var s = fx.Factory.Services.CreateScope();
        var db = Get<IAppDbContext>(s);
        var semis = await db.Matches.Where(m => m.PhaseId == ko && m.Round!.Kind == RoundKind.SemiFinal).OrderBy(m => m.BracketPosition).ToListAsync();
        var schedule = Get<ScheduleService>(s);
        foreach (var (semi, i) in semis.Select((m, i) => (m, i)))
        {
            semi.HomeClubId = clubs[i * 2];
            semi.AwayClubId = clubs[i * 2 + 1];
        }
        await db.SaveChangesAsync();
        var results = Get<ResultService>(s);

        var noPens = () => results.SaveAsync(Score(semis[0].Id, 1, 1));
        (await noPens.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("tirs au but");

        await results.SaveAsync(Score(semis[0].Id, 1, 1, 3, 4)); // l'extérieur gagne aux tirs au but
        await results.SaveAsync(Score(semis[1].Id, 2, 0));

        var final = await db.Matches.AsNoTracking().SingleAsync(m => m.PhaseId == ko && m.Round!.Kind == RoundKind.Final);
        final.HomeClubId.Should().Be(clubs[1]);
        final.AwayClubId.Should().Be(clubs[2]);

        // Correction du score : le vainqueur change, la finale suit.
        await results.SaveAsync(Score(semis[1].Id, 0, 2));
        (await db.Matches.AsNoTracking().SingleAsync(m => m.Id == final.Id)).AwayClubId.Should().Be(clubs[3]);

        // Effacer le résultat libère la place.
        await results.ClearAsync(semis[1].Id);
        (await db.Matches.AsNoTracking().SingleAsync(m => m.Id == final.Id)).AwayClubId.Should().BeNull();
    }

    [Fact]
    public async Task Goals_must_match_the_score_and_players_their_team()
    {
        var (_, groupId, _, _) = await SetupAsync("ev");
        using var s = fx.Factory.Services.CreateScope();
        var db = Get<IAppDbContext>(s);
        var match = await db.Matches.FirstAsync(m => m.GroupId == groupId);
        var results = Get<ResultService>(s);

        var input = Score(match.Id, 2, 0);
        input.Events = [new ResultEventInput { Type = MatchEventType.Goal, ClubId = match.HomeClubId, Minute = 12 }];
        (await FluentActions.Awaiting(() => results.SaveAsync(input)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("ne correspondent pas");

        input.Events.Add(new ResultEventInput { Type = MatchEventType.OwnGoal, ClubId = match.HomeClubId, Minute = 70 });
        input.Events.Add(new ResultEventInput { Type = MatchEventType.YellowCard, ClubId = match.AwayClubId, Minute = 30 });
        (await FluentActions.Awaiting(() => results.SaveAsync(input)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("joueur averti");

        input.Events.RemoveAt(2);
        await results.SaveAsync(input);
        (await db.MatchEvents.CountAsync(e => e.MatchId == match.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Qualification_from_group_ranks_fills_the_next_phase()
    {
        var (_, groupId, ko, _) = await SetupAsync("q");
        using var s = fx.Factory.Services.CreateScope();
        var db = Get<IAppDbContext>(s);
        var quals = Get<QualificationService>(s);
        var semis = await db.Matches.Where(m => m.PhaseId == ko && m.Round!.Kind == RoundKind.SemiFinal).OrderBy(m => m.BracketPosition).ToListAsync();

        // 1er contre 4e, 2e contre 3e.
        await quals.SetSourceAsync(semis[0].Id, MatchSlot.Home, $"g{groupId}:1");
        await quals.SetSourceAsync(semis[0].Id, MatchSlot.Away, $"g{groupId}:4");
        await quals.SetSourceAsync(semis[1].Id, MatchSlot.Home, $"g{groupId}:2");
        await quals.SetSourceAsync(semis[1].Id, MatchSlot.Away, $"g{groupId}:3");
        (await db.Matches.AsNoTracking().SingleAsync(m => m.Id == semis[0].Id)).HomePlaceholder.Should().Be("1er Poule A");

        // Tous les matchs de poule : l'équipe à domicile gagne 1-0.
        var results = Get<ResultService>(s);
        foreach (var m in await db.Matches.AsNoTracking().Where(m => m.GroupId == groupId).ToListAsync())
            await results.SaveAsync(Score(m.Id, 1, 0));

        (await quals.GenerateAsync(ko)).Should().Be(4);
        var table = await Get<StandingsService>(s).GroupRowsAsync(groupId);
        var filled = await db.Matches.AsNoTracking().SingleAsync(m => m.Id == semis[0].Id);
        filled.HomeClubId.Should().Be(table[0].ClubId);
        filled.AwayClubId.Should().Be(table[3].ClubId);

        // Choix manuel de l'admin : conservé lors d'une nouvelle génération.
        var schedule = Get<ScheduleService>(s);
        var input = ScheduleService.ToInput(await schedule.GetAsync(semis[1].Id));
        input.HomeClubId = table[3].ClubId == input.AwayClubId ? table[0].ClubId : table[3].ClubId;
        if (input.HomeClubId == input.AwayClubId) input.HomeClubId = table[1].ClubId == input.AwayClubId ? table[0].ClubId : table[1].ClubId;
        input.KickoffLocal = null;
        await schedule.SaveAsync(input);
        await quals.GenerateAsync(ko);
        (await db.Matches.AsNoTracking().SingleAsync(m => m.Id == semis[1].Id)).HomeClubId.Should().Be(input.HomeClubId);
    }

    [Fact]
    public async Task Admin_result_form_round_trip()
    {
        var (_, groupId, _, _) = await SetupAsync("form");
        var matchId = await fx.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<IAppDbContext>()
            .Matches.Where(m => m.GroupId == groupId).Select(m => m.Id).FirstAsync();
        var client = fx.Factory.ClientAs(RolesForTests.Admin);
        var page = await client.GetStringAsync($"/admin/matchs/{matchId}/resultat");
        var token = Token().Match(page).Groups[1].Value;
        var res = await client.PostAsync($"/admin/matchs/{matchId}/resultat", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Status"] = "5", ["HomeScore"] = "3", ["AwayScore"] = "1", ["__RequestVerificationToken"] = token
        }));
        res.StatusCode.Should().Be(HttpStatusCode.Redirect, await res.Content.ReadAsStringAsync());
        using var scope = fx.Factory.Services.CreateScope();
        var m = await scope.ServiceProvider.GetRequiredService<IAppDbContext>().Matches.AsNoTracking().SingleAsync(x => x.Id == matchId);
        (m.Status, m.HomeScore, m.AwayScore).Should().Be((MatchStatus.Finished, 3, 1));
    }

    [GeneratedRegex("__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex Token();
}
