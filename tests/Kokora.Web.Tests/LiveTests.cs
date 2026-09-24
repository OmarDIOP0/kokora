using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Application.Live;
using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

public class LiveFixture() : ServicesFixture("kokora_tests_live");

public class LiveMatchServiceTests(LiveFixture fx) : IClassFixture<LiveFixture>
{
    private record Setup(int MatchId, int Home, int Away, int[] HomePlayers, int[] AwayPlayers);

    /// <summary>Un match à élimination directe (tirs au but, sans prolongations) entre deux équipes de 3 joueurs.</summary>
    private async Task<Setup> CreateKnockoutMatchAsync(string tag)
    {
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var season = new Season { Year = tag switch { "ko" => 2071, "dup" => 2072, "min" => 2073, "rt" => 2074, _ => 2075 }, Name = $"Direct {tag}" };
        var comp = new Competition { Season = season, Name = $"Coupe {tag}", ShortName = tag, Slug = $"coupe-{tag}" };
        var phase = new Phase { Competition = comp, Name = "Finale", Type = PhaseType.Knockout, HasPenalties = true };
        var round = new Round { Phase = phase, Name = "Finale", Kind = RoundKind.Final, Order = 1 };
        Club Club(string n) => new() { Name = $"ASC {n} {tag}", ShortName = $"{n}{tag}", Slug = $"asc-{n}-{tag}".ToLower() };
        var (home, away) = (Club("H"), Club("A"));
        List<Player> Players(Club c) => Enumerable.Range(1, 3).Select(i => new Player
        {
            FirstName = $"J{i}", LastName = c.ShortName, Slug = $"j{i}-{c.Slug}"
        }).ToList();
        var (hp, ap) = (Players(home), Players(away));
        db.SquadMembers.AddRange(hp.Select((p, i) => new SquadMember { Season = season, Club = home, Player = p, ShirtNumber = i + 1 }));
        db.SquadMembers.AddRange(ap.Select((p, i) => new SquadMember { Season = season, Club = away, Player = p, ShirtNumber = i + 1 }));
        var match = new Match { Phase = phase, Round = round, HomeClub = home, AwayClub = away, KickoffAt = DateTimeOffset.UtcNow };
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        return new Setup(match.Id, home.Id, away.Id, hp.Select(p => p.Id).ToArray(), ap.Select(p => p.Id).ToArray());
    }

    private (LiveMatchService Live, IServiceScope Scope) Service()
    {
        var scope = fx.Factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<LiveMatchService>(), scope);
    }

    [Fact]
    public async Task Full_knockout_match_with_penalties()
    {
        var m = await CreateKnockoutMatchAsync("ko");
        var (live, scope) = Service();
        using var _ = scope;

        await live.AdvanceAsync(m.MatchId, LivePeriod.FirstHalf);
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.Goal, ClubId = m.Home, PlayerId = m.HomePlayers[0], AssistPlayerId = m.HomePlayers[1], Minute = 12 });
        // But contre son camp : l'équipe bénéficiaire est « A », le buteur joue pour « H ».
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.OwnGoal, ClubId = m.Away, PlayerId = m.HomePlayers[2], Minute = 30 });
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.YellowCard, ClubId = m.Away, PlayerId = m.AwayPlayers[0] });
        await live.AdvanceAsync(m.MatchId, LivePeriod.HalfTime);
        var state = await live.StateAsync(m.MatchId);
        state.Status.Should().Be(MatchStatus.HalfTime);
        (state.HomeScore, state.AwayScore).Should().Be((1, 1));

        await live.AdvanceAsync(m.MatchId, LivePeriod.SecondHalf);
        // Deuxième jaune du même joueur : transformé en exclusion.
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.YellowCard, ClubId = m.Away, PlayerId = m.AwayPlayers[0] });
        state = await live.StateAsync(m.MatchId);
        state.Events.Should().Contain(e => e.Type == MatchEventType.SecondYellow);
        var again = () => live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.RedCard, ClubId = m.Away, PlayerId = m.AwayPlayers[0] });
        await again.Should().ThrowAsync<BusinessRuleException>().WithMessage("*déjà été exclu*");

        // Égalité en élimination directe : pas de fin directe, tirs au but.
        state.Next.Select(n => n.To).Should().Equal(LivePeriod.Penalties);
        var tooSoon = () => live.AdvanceAsync(m.MatchId, LivePeriod.Ended);
        await tooSoon.Should().ThrowAsync<BusinessRuleException>();
        await live.AdvanceAsync(m.MatchId, LivePeriod.Penalties);
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.ShootoutKick, ClubId = m.Home, IsScored = true });
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.ShootoutKick, ClubId = m.Away, IsScored = true });
        var tied = () => live.AdvanceAsync(m.MatchId, LivePeriod.Ended);
        await tied.Should().ThrowAsync<BusinessRuleException>().WithMessage("*vainqueur*");
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.ShootoutKick, ClubId = m.Home, IsScored = true });
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.ShootoutKick, ClubId = m.Away, IsScored = false });
        await live.AdvanceAsync(m.MatchId, LivePeriod.Ended);

        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var match = await db.Matches.AsNoTracking().SingleAsync(x => x.Id == m.MatchId);
        match.Status.Should().Be(MatchStatus.Finished);
        (match.HomeScore, match.AwayScore, match.HomeHalfTimeScore, match.AwayHalfTimeScore).Should().Be((1, 1, 1, 1));
        (match.HomePenalties, match.AwayPenalties).Should().Be((2, 1));
        Kokora.Application.Admin.ResultService.Winner(match).Should().Be(m.Home);
    }

    [Fact]
    public async Task Duplicate_actions_and_cancellations()
    {
        var m = await CreateKnockoutMatchAsync("dup");
        var (live, scope) = Service();
        using var _ = scope;
        await live.AdvanceAsync(m.MatchId, LivePeriod.FirstHalf);
        await live.AdvanceAsync(m.MatchId, LivePeriod.FirstHalf); // renvoi : sans effet

        var goal = new LiveEventInput { Type = MatchEventType.Goal, ClubId = m.Home, PlayerId = m.HomePlayers[0], ClientKey = "cle-unique-1" };
        await live.AddEventAsync(m.MatchId, goal);
        await live.AddEventAsync(m.MatchId, goal); // même clé : ignoré
        var state = await live.StateAsync(m.MatchId);
        state.HomeScore.Should().Be(1);
        state.Events.Should().ContainSingle();

        await live.CancelEventAsync(m.MatchId, state.Events[0].Id);
        state = await live.StateAsync(m.MatchId);
        (state.HomeScore, state.Events.Count).Should().Be((0, 0));

        var wrongTeam = () => live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.Goal, ClubId = m.Home, PlayerId = m.AwayPlayers[0] });
        await wrongTeam.Should().ThrowAsync<BusinessRuleException>();
        var kickTooEarly = () => live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.ShootoutKick, ClubId = m.Home, IsScored = true });
        await kickTooEarly.Should().ThrowAsync<BusinessRuleException>();
        var skip = () => live.AdvanceAsync(m.MatchId, LivePeriod.SecondHalf);
        await skip.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Minute_follows_the_clock_and_can_be_corrected()
    {
        var m = await CreateKnockoutMatchAsync("min");
        var (live, scope) = Service();
        using var _ = scope;
        await live.AdvanceAsync(m.MatchId, LivePeriod.FirstHalf);
        await live.SetMinuteAsync(m.MatchId, 38);
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.YellowCard, ClubId = m.Home, PlayerId = m.HomePlayers[1] });
        (await live.StateAsync(m.MatchId)).Events.Single().Minute.Should().Be("38'");
        var invalid = () => live.SetMinuteAsync(m.MatchId, 0);
        await invalid.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Spectators_receive_updates_in_real_time()
    {
        var m = await CreateKnockoutMatchAsync("rt");
        var server = fx.Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/direct/hub"), o =>
            {
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var received = new TaskCompletionSource<LiveUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<LiveUpdate>("match", u => { if (u.MatchId == m.MatchId && u.HomeScore == 1) received.TrySetResult(u); });
        await connection.StartAsync();

        var (live, scope) = Service();
        using (scope)
        {
            await live.AdvanceAsync(m.MatchId, LivePeriod.FirstHalf);
            await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.Goal, ClubId = m.Home });
        }
        var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        update.Status.Should().Be(MatchStatus.Live);
        update.Text.Should().StartWith("But pour");
        await connection.DisposeAsync();

        // Fragments publics rafraîchis par la page.
        var client = fx.Factory.ClientAs(null);
        (await client.GetAsync($"/matchs/{m.MatchId}/ligne")).StatusCode.Should().Be(HttpStatusCode.OK);
        var fragments = await client.GetStringAsync($"/matchs/{m.MatchId}/direct");
        fragments.Should().Contain("id=\"match-head\"").And.Contain("hx-swap-oob=\"true\"").And.Contain("data-clock");
    }

    [Fact]
    public async Task Field_mode_api_returns_state_and_errors()
    {
        var m = await CreateKnockoutMatchAsync("api");
        var admin = fx.Factory.ClientAs(RolesForTests.Admin);
        (await admin.GetAsync("/admin/direct")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/admin/direct/{m.MatchId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Jeton anti-CSRF : lu sur la page, renvoyé en en-tête comme le fait le navigateur.
        var page = await admin.GetAsync($"/admin/direct/{m.MatchId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"csrf-token\" content=\"([^\"]+)\"").Groups[1].Value;
        var cookies = page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]);
        HttpRequestMessage Post(string url, object body)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
            req.Headers.Add("RequestVerificationToken", token);
            req.Headers.Add("Cookie", string.Join("; ", cookies));
            return req;
        }

        var ok = await admin.SendAsync(Post($"/admin/direct/{m.MatchId}/periode", new { to = (int)LivePeriod.FirstHalf }));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Contain("\"status\":3");
        var refused = await admin.SendAsync(Post($"/admin/direct/{m.MatchId}/periode", new { to = (int)LivePeriod.Penalties }));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Action impossible");
        (await fx.Factory.ClientAs(null).GetAsync($"/admin/direct/{m.MatchId}")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
}
