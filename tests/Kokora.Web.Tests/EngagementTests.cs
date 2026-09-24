using System.Net;
using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Application.Engagement;
using Kokora.Application.Live;
using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Infrastructure.Identity;
using Kokora.Web.Controllers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

public class EngagementUnitTests
{
    [Theory]
    [InlineData(2, 1, 2, 1, 3)]  // score exact
    [InlineData(1, 0, 3, 1, 1)]  // bon vainqueur
    [InlineData(1, 1, 0, 0, 1)]  // nul prévu, nul joué
    [InlineData(0, 2, 1, 0, 0)]  // mauvais résultat
    public void Prediction_points(int ph, int pa, int h, int a, int expected) =>
        PredictionService.Score(ph, pa, h, a).Should().Be(expected);

    [Theory]
    [InlineData("77 123 45 67", "+221771234567")]
    [InlineData("+221 77 123 45 67", "+221771234567")]
    [InlineData("00221781234567", "+221781234567")]
    [InlineData("338001122", "+221338001122")]
    public void Senegalese_phone_numbers_are_normalized(string input, string expected) =>
        LoginId.Parse(input)!.Phone.Should().Be(expected);

    [Theory]
    [InlineData("12345")]
    [InlineData("612345678")]
    [InlineData("pas-un-email@")]
    public void Invalid_logins_are_rejected(string input) => LoginId.Parse(input).Should().BeNull();

    [Fact]
    public void Emails_are_lowercased() => LoginId.Parse(" Awa@Exemple.SN ")!.Email.Should().Be("awa@exemple.sn");
}

public class EngagementFixture() : ServicesFixture("kokora_tests_engagement");

public class EngagementServiceTests(EngagementFixture fx) : IClassFixture<EngagementFixture>
{
    private record Setup(int SeasonId, int MatchId, int Home, int Away, int[] HomePlayers, int[] AwayPlayers);

    private async Task<Setup> MatchAsync(int year, DateTimeOffset kickoff)
    {
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var tag = year.ToString();
        var season = new Season { Year = year, Name = $"Engagement {tag}", IsCurrent = false };
        var comp = new Competition { Season = season, Name = $"Coupe {tag}", ShortName = tag, Slug = $"coupe-{tag}" };
        var phase = new Phase { Competition = comp, Name = "Poules", Type = PhaseType.League };
        Club Club(string n) => new() { Name = $"ASC {n}{tag}", ShortName = $"{n}{tag}", Slug = $"asc-{n}-{tag}".ToLower() };
        var (home, away) = (Club("H"), Club("A"));
        List<Player> Players(Club c) => Enumerable.Range(1, 4).Select(i => new Player { FirstName = $"J{i}", LastName = c.ShortName, Slug = $"j{i}-{c.Slug}" }).ToList();
        var (hp, ap) = (Players(home), Players(away));
        db.SquadMembers.AddRange(hp.Select((p, i) => new SquadMember { Season = season, Club = home, Player = p, ShirtNumber = i + 1 }));
        db.SquadMembers.AddRange(ap.Select((p, i) => new SquadMember { Season = season, Club = away, Player = p, ShirtNumber = i + 1 }));
        var match = new Match { Phase = phase, HomeClub = home, AwayClub = away, KickoffAt = kickoff };
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        return new Setup(season.Id, match.Id, home.Id, away.Id, hp.Select(p => p.Id).ToArray(), ap.Select(p => p.Id).ToArray());
    }

    private T Get<T>(IServiceScope s) where T : notnull => s.ServiceProvider.GetRequiredService<T>();

    [Fact]
    public async Task Predictions_are_scored_and_ranked()
    {
        var m = await MatchAsync(2081, DateTimeOffset.UtcNow.AddHours(3));
        using var scope = fx.Factory.Services.CreateScope();
        var predictions = Get<PredictionService>(scope);
        await predictions.SaveAsync(m.MatchId, "u-exact", 2, 1);
        await predictions.SaveAsync(m.MatchId, "u-winner", 1, 0);
        await predictions.SaveAsync(m.MatchId, "u-wrong", 0, 0);
        await predictions.SaveAsync(m.MatchId, "u-wrong", 0, 3); // modifiable avant le coup d'envoi

        var block = await predictions.BlockAsync(m.MatchId, "u-exact");
        block!.Open.Should().BeTrue();
        block.Mine.Should().Be(new PredictionVm(2, 1, null));
        (block.Summary.Count, block.Summary.HomePct, block.Summary.AwayPct).Should().Be((3, 67, 33));

        // Résultat saisi : 2-1.
        await Get<ResultService>(scope).SaveAsync(new ResultInput { MatchId = m.MatchId, Status = MatchStatus.Finished, HomeScore = 2, AwayScore = 1 });
        var board = await predictions.LeaderboardAsync(m.SeasonId, "u-wrong");
        board.Top.Select(r => (r.UserId, r.Points, r.Rank)).Should().Equal(("u-exact", 3, 1), ("u-winner", 1, 2), ("u-wrong", 0, 3));
        board.Me!.Rank.Should().Be(3);

        var closed = () => predictions.SaveAsync(m.MatchId, "u-late", 1, 1);
        await closed.Should().ThrowAsync<BusinessRuleException>().WithMessage("*fermés*");

        // Résultat effacé : les points sont retirés.
        await Get<ResultService>(scope).ClearAsync(m.MatchId);
        (await predictions.LeaderboardAsync(m.SeasonId, null)).Top.Should().BeEmpty();
    }

    [Fact]
    public async Task Man_of_the_match_vote_is_open_after_the_final_whistle()
    {
        var m = await MatchAsync(2082, DateTimeOffset.UtcNow.AddMinutes(-100));
        using var scope = fx.Factory.Services.CreateScope();
        var votes = Get<VoteService>(scope);
        (await votes.BlockAsync(m.MatchId, null)).Should().BeNull(); // match pas encore joué

        await Get<ResultService>(scope).SaveAsync(new ResultInput { MatchId = m.MatchId, Status = MatchStatus.Finished, HomeScore = 1, AwayScore = 0 });
        await votes.VoteAsync(m.MatchId, "v1", m.HomePlayers[0]);
        await votes.VoteAsync(m.MatchId, "v2", m.HomePlayers[0]);
        await votes.VoteAsync(m.MatchId, "v3", m.AwayPlayers[1]);
        await votes.VoteAsync(m.MatchId, "v3", m.HomePlayers[2]); // vote modifié

        var block = await votes.BlockAsync(m.MatchId, "v3");
        block!.Open.Should().BeTrue();
        block.Total.Should().Be(3);
        block.MyVote.Should().Be(m.HomePlayers[2]);
        block.Results[0].Should().Match<VoteResult>(r => r.PlayerId == m.HomePlayers[0] && r.Votes == 2 && r.Pct == 67 && r.IsHome);

        var outsider = await MatchAsync(2083, DateTimeOffset.UtcNow.AddDays(-1));
        var wrong = () => votes.VoteAsync(m.MatchId, "v4", outsider.HomePlayers[0]);
        await wrong.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Goals_and_results_notify_followers_only()
    {
        var m = await MatchAsync(2084, DateTimeOffset.UtcNow.AddMinutes(5));
        using var scope = fx.Factory.Services.CreateScope();
        var notifications = Get<NotificationService>(scope);
        await notifications.SubscribeAsync(new SubscriptionInput { Endpoint = "https://push.exemple/abonne-1", P256dh = "k", Auth = "a", ClubIds = [m.Home] }, null, null);
        await notifications.SubscribeAsync(new SubscriptionInput { Endpoint = "https://push.exemple/abonne-2", P256dh = "k", Auth = "a", ClubIds = [m.Away], NotifyGoals = false }, null, null);
        await notifications.SubscribeAsync(new SubscriptionInput { Endpoint = "https://push.exemple/autre", P256dh = "k", Auth = "a", ClubIds = [999999] }, null, null);
        var ids = await Get<IAppDbContext>(scope).PushSubscriptions.Where(s => s.Endpoint.StartsWith("https://push.exemple/abonne"))
            .OrderBy(s => s.Endpoint).Select(s => s.Id).ToListAsync();
        var push = fx.Factory.Services.GetRequiredService<RecordingPushService>();
        var before = push.Sent.Count;

        var live = Get<LiveMatchService>(scope);
        await live.AdvanceAsync(m.MatchId, LivePeriod.FirstHalf);
        await live.AddEventAsync(m.MatchId, new LiveEventInput { Type = MatchEventType.Goal, ClubId = m.Home, PlayerId = m.HomePlayers[3] });
        await live.AdvanceAsync(m.MatchId, LivePeriod.HalfTime);
        await live.AdvanceAsync(m.MatchId, LivePeriod.SecondHalf);
        await live.AdvanceAsync(m.MatchId, LivePeriod.Ended);

        var sent = push.Sent.Skip(before).Where(s => s.Message.Tag == $"match-{m.MatchId}").ToList();
        sent.Select(s => s.Message.Title).Should().Equal("Coup d'envoi", $"But ! H2084 1-0 A2084", "Terminé : H2084 1-0 A2084");
        sent[0].Ids.Should().BeEquivalentTo(ids);            // coup d'envoi : les deux abonnés
        sent[1].Ids.Should().BeEquivalentTo([ids[0]]);       // but : l'abonné 2 a désactivé les buts
        sent[1].Message.Body.Should().Contain("J4 H2084");
        await notifications.UnsubscribeAsync("https://push.exemple/abonne-1");
    }

    [Fact]
    public async Task Important_news_is_notified_once()
    {
        using var scope = fx.Factory.Services.CreateScope();
        await Get<NotificationService>(scope).SubscribeAsync(new SubscriptionInput { Endpoint = "https://push.exemple/infos", P256dh = "k", Auth = "a" }, null, null);
        var push = fx.Factory.Services.GetRequiredService<RecordingPushService>();
        var articles = Get<ArticleAdminService>(scope);
        var id = await articles.SaveAsync(new ArticleInput { Title = "Report de la finale", Body = "<p>Texte</p>", Mode = PublishMode.Now, IsImportant = true }, null);
        await articles.SaveAsync(new ArticleInput { Id = id, Title = "Report de la finale (mise à jour)", Body = "<p>Texte</p>", Mode = PublishMode.Now, IsImportant = true }, null);
        push.Sent.Count(s => s.Message.Tag == $"info-{id}").Should().Be(1);
    }

    [Fact]
    public async Task Comments_are_moderated_until_trusted()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var articleId = await Get<ArticleAdminService>(scope).SaveAsync(new ArticleInput { Title = "Info commentée", Body = "<p>Texte</p>", Mode = PublishMode.Now }, null);
        var comments = Get<CommentService>(scope);

        (await comments.PostAsync(articleId, "c-user", "Awa", "Premier commentaire")).Should().Be(CommentStatus.Pending);
        (await comments.ListAsync(articleId, null)).Should().BeEmpty();                       // invisible pour les autres
        (await comments.ListAsync(articleId, "c-user")).Should().ContainSingle(c => c.IsMine); // visible par son auteur

        foreach (var c in await comments.ModerationAsync(CommentStatus.Pending))
            await comments.SetStatusAsync(c.Id, CommentStatus.Approved);
        for (var i = 0; i < 2; i++) await comments.PostAsync(articleId, "c-user", "Awa", $"Commentaire {i}");
        foreach (var c in await comments.ModerationAsync(CommentStatus.Pending))
            await comments.SetStatusAsync(c.Id, CommentStatus.Approved);

        // 3 commentaires validés : publication immédiate.
        (await comments.PostAsync(articleId, "c-user", "Awa", "Quatrième")).Should().Be(CommentStatus.Approved);
        await comments.PostAsync(articleId, "c-user", "Awa", "Cinquième");
        var flood = () => comments.PostAsync(articleId, "c-user", "Awa", "Sixième en 10 minutes");
        await flood.Should().ThrowAsync<BusinessRuleException>().WithMessage("*patientez*");
        var empty = () => comments.PostAsync(articleId, "c-other", "Moussa", " ");
        await empty.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Favorites_merge_at_first_sync_then_replace()
    {
        var m = await MatchAsync(2085, DateTimeOffset.UtcNow.AddDays(3));
        using var scope = fx.Factory.Services.CreateScope();
        var data = Get<AccountDataService>(scope);
        await data.SaveFavoritesAsync("f-user", [m.Home], merge: false);
        (await data.SaveFavoritesAsync("f-user", [m.Away, 999999], merge: true)).Should().BeEquivalentTo([m.Home, m.Away]);
        (await data.SaveFavoritesAsync("f-user", [m.Away], merge: false)).Should().Equal(m.Away);
    }
}

[Collection(DemoCollection.Name)]
public class AccountPagesTests(DemoDataFixture fx)
{
    [Fact]
    public async Task Registration_creates_a_supporter_account()
    {
        var client = fx.Factory.ClientAs(null);
        var page = await client.GetAsync("/compte/inscription");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var cookies = string.Join("; ", page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        async Task<HttpResponseMessage> Register(string login, string password)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/compte/inscription")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["DisplayName"] = "Awa Test", ["Login"] = login, ["Password"] = password, ["ConfirmPassword"] = password,
                    ["__RequestVerificationToken"] = token
                })
            };
            req.Headers.Add("Cookie", cookies);
            return await client.SendAsync(req);
        }

        (await Register("77 000 11 22", "motdepasse1")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByNameAsync("+221770001122");
            user.Should().NotBeNull();
            (await users.GetRolesAsync(user!)).Should().Equal(Roles.User);
        }
        var duplicate = await Register("+221770001122", "motdepasse1");
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("Un compte existe déjà");
        var weak = await Register("awa@exemple.sn", "court");
        (await weak.Content.ReadAsStringAsync()).Should().Contain("8 caractères minimum");
    }

    [Fact]
    public async Task Engagement_pages_render()
    {
        // Compte correspondant à l'utilisateur simulé par TestAuthHandler.
        using (var scope = fx.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await users.FindByIdAsync("test-user") is null)
                await users.CreateAsync(new AppUser { Id = "test-user", UserName = "testeur@kokora.test", DisplayName = "Testeur" });
        }
        var match = await fx.QueryAsync(db => db.Matches.Where(m => m.IsDemo && m.Status == MatchStatus.Finished).Select(m => m.Id).FirstAsync());
        var article = await fx.QueryAsync(db => db.Articles.Where(a => a.IsDemo && a.Status == ArticleStatus.Published).Select(a => a.Slug).FirstAsync());
        foreach (var (role, url) in new (string?, string)[]
                 {
                     (null, "/pronostics"), (RolesForTests.User, "/pronostics"), (RolesForTests.User, "/compte"), (null, "/compte/inscription"),
                     (null, $"/matchs/{match}"), (RolesForTests.User, $"/matchs/{match}"), (RolesForTests.User, $"/infos/{article}"), (null, "/plus"),
                     (RolesForTests.Editor, "/admin/commentaires"), (RolesForTests.Admin, "/admin/utilisateurs"), (RolesForTests.Admin, "/admin/notifications"),
                     (null, "/notifications/cle")
                 })
        {
            var client = fx.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
            if (role is not null) client.DefaultRequestHeaders.Add(KokoraWebFactory.RoleHeader, role);
            var res = await client.GetAsync(url);
            var body = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} {url} : {body[..Math.Min(body.Length, 2000)]}");
        }
        (await fx.Factory.ClientAs(RolesForTests.Editor).GetAsync("/admin/utilisateurs")).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
