using FluentAssertions;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Web.Tests;

/// <summary>Base dédiée, vide au départ : chaque test crée ses propres données. Une base par classe de tests.</summary>
public class ServicesFixture : IDisposable
{
    public KokoraWebFactory Factory { get; }
    public ServicesFixture() : this("kokora_tests_services") { }
    protected ServicesFixture(string database)
    {
        Factory = new KokoraWebFactory(database);
        _ = Factory.Server;
    }
    public void Dispose() => Factory.Dispose();
}

public class ResultsFixture() : ServicesFixture("kokora_tests_results");

public class AdminServicesTests(ServicesFixture fx) : IClassFixture<ServicesFixture>
{
    private sealed class Scope(IServiceScope scope) : IDisposable
    {
        public T Get<T>() where T : notnull => scope.ServiceProvider.GetRequiredService<T>();
        public IAppDbContext Db => Get<IAppDbContext>();
        public void Dispose() => scope.Dispose();
    }

    private Scope NewScope() => new(fx.Factory.Services.CreateScope());

    private static int _year = 2040;

    /// <summary>Saison + compétition + phase + n équipes, noms uniques par test.</summary>
    private async Task<(int SeasonId, int CompetitionId, int PhaseId, List<int> Clubs)> SetupAsync(PhaseType type, int teams, string tag)
    {
        using var s = NewScope();
        var seasonId = await s.Get<SeasonAdminService>().SaveAsync(new SeasonInput { Year = Interlocked.Increment(ref _year), Name = $"Test {tag}" });
        var comps = s.Get<CompetitionAdminService>();
        var compId = await comps.SaveAsync(new CompetitionInput { SeasonId = seasonId, Name = $"Coupe {tag}", ShortName = tag });
        var phaseId = await comps.SavePhaseAsync(new PhaseInput { CompetitionId = compId, Name = "Phase", Type = type });
        var clubs = new List<int>();
        for (var i = 1; i <= teams; i++)
            clubs.Add(await s.Get<ClubAdminService>().SaveAsync(new ClubInput { Name = $"ASC Test {tag} {i}", ShortName = $"{tag}{i}" }, null));
        return (seasonId, compId, phaseId, clubs);
    }

    [Fact]
    public async Task Knockout_bracket_links_every_winner_to_the_next_round()
    {
        var (_, _, phaseId, _) = await SetupAsync(PhaseType.Knockout, 0, "ko");
        using var s = NewScope();
        await s.Get<CompetitionAdminService>().CreateKnockoutBracketAsync(new KnockoutInput { PhaseId = phaseId, TeamCount = 8, ThirdPlace = true });

        var rounds = await s.Db.Rounds.Where(r => r.PhaseId == phaseId).OrderBy(r => r.Order).ToListAsync();
        rounds.Select(r => r.Kind).Should().Equal(RoundKind.QuarterFinal, RoundKind.SemiFinal, RoundKind.Final, RoundKind.ThirdPlace);
        (await s.Db.Matches.CountAsync(m => m.PhaseId == phaseId)).Should().Be(4 + 2 + 1 + 1);

        var quals = await s.Db.Qualifications.Where(q => q.TargetPhaseId == phaseId).ToListAsync();
        quals.Count(q => q.Source == QualificationSource.MatchWinner).Should().Be(6);
        quals.Count(q => q.Source == QualificationSource.MatchLoser).Should().Be(2);
        // Chaque place du tour suivant est alimentée une seule fois.
        quals.Select(q => (q.TargetMatchId, q.TargetSlot)).Should().OnlyHaveUniqueItems();

        var final = await s.Db.Matches.SingleAsync(m => m.PhaseId == phaseId && m.Round!.Kind == RoundKind.Final);
        final.HomePlaceholder.Should().Be("Vainqueur DF1");
        final.AwayPlaceholder.Should().Be("Vainqueur DF2");

        // Recréer sans supprimer est refusé ; supprimer puis recréer fonctionne.
        var again = () => s.Get<CompetitionAdminService>().CreateKnockoutBracketAsync(new KnockoutInput { PhaseId = phaseId, TeamCount = 4 });
        await again.Should().ThrowAsync<BusinessRuleException>();
        await s.Get<CompetitionAdminService>().ClearKnockoutBracketAsync(phaseId);
        await again();
        (await s.Db.Matches.CountAsync(m => m.PhaseId == phaseId)).Should().Be(3);
    }

    [Fact]
    public async Task A_team_cannot_be_in_two_groups_of_the_same_phase()
    {
        var (_, _, phaseId, clubs) = await SetupAsync(PhaseType.League, 6, "grp");
        using var s = NewScope();
        var comps = s.Get<CompetitionAdminService>();
        await comps.SaveGroupAsync(new GroupInput { PhaseId = phaseId, Name = "Poule A", ClubIds = clubs[..3], QualifiedCount = 2 });

        var act = () => comps.SaveGroupAsync(new GroupInput { PhaseId = phaseId, Name = "Poule B", ClubIds = clubs[2..] });
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("Poule A");
    }

    [Fact]
    public async Task Group_fixtures_are_generated_once_unless_replaced()
    {
        var (_, _, phaseId, clubs) = await SetupAsync(PhaseType.League, 4, "gen");
        using var s = NewScope();
        var groupId = await s.Get<CompetitionAdminService>().SaveGroupAsync(new GroupInput { PhaseId = phaseId, Name = "Poule A", ClubIds = clubs, QualifiedCount = 2 });
        var schedule = s.Get<ScheduleService>();
        var input = new FixtureGenerationInput
        {
            GroupId = groupId, HomeAndAway = true, FirstDate = new DateOnly(2040, 8, 1), DaysBetweenMatchdays = 7,
            FirstKickoff = new TimeOnly(16, 30), MinutesBetweenMatches = 120
        };

        (await schedule.GenerateGroupFixturesAsync(input)).Should().Be(12);
        var first = await s.Db.Matches.Where(m => m.GroupId == groupId && m.Matchday == 1).OrderBy(m => m.KickoffAt).ToListAsync();
        first.Should().HaveCount(2);
        KokoraTime.Hour(first[0].KickoffAt!.Value).Should().Be("16h30");
        KokoraTime.Hour(first[1].KickoffAt!.Value).Should().Be("18h30");

        await FluentActions.Awaiting(() => schedule.GenerateGroupFixturesAsync(input)).Should().ThrowAsync<BusinessRuleException>();
        input.ReplaceUnplayed = true;
        (await schedule.GenerateGroupFixturesAsync(input)).Should().Be(12);
        (await s.Db.Matches.CountAsync(m => m.GroupId == groupId)).Should().Be(12);
    }

    [Fact]
    public async Task A_team_cannot_play_twice_the_same_day_nor_against_itself()
    {
        var (_, _, phaseId, clubs) = await SetupAsync(PhaseType.League, 3, "day");
        using var s = NewScope();
        var groupId = await s.Get<CompetitionAdminService>().SaveGroupAsync(new GroupInput { PhaseId = phaseId, Name = "Poule A", ClubIds = clubs });
        var schedule = s.Get<ScheduleService>();
        var day = new DateTime(2040, 9, 5, 16, 0, 0);

        await schedule.SaveAsync(new MatchInput { PhaseId = phaseId, GroupId = groupId, HomeClubId = clubs[0], AwayClubId = clubs[1], KickoffLocal = day });

        var sameDay = () => schedule.SaveAsync(new MatchInput { PhaseId = phaseId, GroupId = groupId, HomeClubId = clubs[1], AwayClubId = clubs[2], KickoffLocal = day.AddHours(3) });
        (await sameDay.Should().ThrowAsync<BusinessRuleException>()).Which.Field.Should().Be(nameof(MatchInput.KickoffLocal));

        var itself = () => schedule.SaveAsync(new MatchInput { PhaseId = phaseId, GroupId = groupId, HomeClubId = clubs[2], AwayClubId = clubs[2], KickoffLocal = day.AddDays(1) });
        await itself.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Postponing_keeps_a_trace_of_the_original_date()
    {
        var (_, _, phaseId, clubs) = await SetupAsync(PhaseType.League, 2, "rep");
        using var s = NewScope();
        var groupId = await s.Get<CompetitionAdminService>().SaveGroupAsync(new GroupInput { PhaseId = phaseId, Name = "Poule A", ClubIds = clubs });
        var schedule = s.Get<ScheduleService>();
        var id = await schedule.SaveAsync(new MatchInput { PhaseId = phaseId, GroupId = groupId, HomeClubId = clubs[0], AwayClubId = clubs[1], KickoffLocal = new DateTime(2040, 9, 12, 16, 30, 0) });

        await schedule.PostponeAsync(id, null);
        (await s.Db.Matches.AsNoTracking().SingleAsync(m => m.Id == id)).Status.Should().Be(MatchStatus.Postponed);

        await schedule.PostponeAsync(id, new DateTime(2040, 9, 19, 17, 0, 0));
        var m = await s.Db.Matches.AsNoTracking().SingleAsync(x => x.Id == id);
        m.Status.Should().Be(MatchStatus.Scheduled);
        KokoraTime.Long(m.KickoffAt!.Value).Should().Be("Mercredi 19 septembre, 17h");
        m.Notes.Should().Contain("Mercredi 12 septembre, 16h30");
    }

    [Fact]
    public async Task Player_import_creates_updates_and_reports_errors_per_line()
    {
        var (seasonId, _, _, clubs) = await SetupAsync(PhaseType.League, 1, "imp");
        using var s = NewScope();
        var clubName = (await s.Db.Clubs.FindAsync(clubs[0]))!.Name;
        var players = s.Get<PlayerAdminService>();
        PlayerImportRow Row(int line, string first, string last, string? club, string? num, string? pos = "Attaquant", string? birth = null) =>
            new(line, first, last, null, pos, club, num, birth, null);

        var report = await players.ImportAsync(
        [
            Row(2, "moussa", "EXEMPLE", clubName, "9", birth: "15/03/2004"),
            Row(3, "Ibou", "Test", "IMP1", "10", "M"),           // équipe par nom court
            Row(4, "Inconnu", "Club", "ASC Qui N'Existe Pas", "5"),
            Row(5, "Autre", "Joueur", clubName, "9"),             // numéro déjà pris
            Row(6, "", "SansPrenom", clubName, null),
        ], seasonId);

        report.Created.Should().Be(2);
        report.Errors.Should().HaveCount(3);
        report.Errors.Should().Contain(e => e.StartsWith("Ligne 4")).And.Contain(e => e.StartsWith("Ligne 5")).And.Contain(e => e.StartsWith("Ligne 6"));

        var moussa = await s.Db.Players.AsNoTracking().SingleAsync(p => p.LastName == "Exemple");
        moussa.FirstName.Should().Be("Moussa");
        moussa.Position.Should().Be(PlayerPosition.Forward);
        moussa.BirthDate.Should().Be(new DateOnly(2004, 3, 15));

        // Réimport : mise à jour, pas de doublon.
        var again = await players.ImportAsync([Row(2, "Moussa", "Exemple", clubName, "9", "D")], seasonId);
        again.Updated.Should().Be(1);
        again.Created.Should().Be(0);
        (await s.Db.Players.CountAsync(p => p.LastName == "Exemple")).Should().Be(1);
    }

    [Fact]
    public async Task Demo_data_can_be_created_and_purged_without_touching_real_data()
    {
        var (_, _, _, realClubs) = await SetupAsync(PhaseType.League, 1, "real");
        using var s = NewScope();
        var demo = s.Get<DemoDataService>();
        await demo.SeedAsync();
        var counts = await demo.CountAsync();
        counts.Clubs.Should().Be(17);
        counts.Players.Should().Be(17 * 14);
        counts.Matches.Should().Be(12 + 12 + 20 + 12 + 3); // poules (4 = 12 matchs, 5 = 20) + demies et finale des 4 Grandes 5A
        (await s.Db.Matches.CountAsync(m => m.IsDemo && m.Status == MatchStatus.Finished)).Should().BeGreaterThan(0);
        // Buts cohérents avec le score.
        var played = await s.Db.Matches.Include(m => m.Events).Where(m => m.IsDemo && m.Status == MatchStatus.Finished).ToListAsync();
        played.Should().OnlyContain(m => m.Events.Count(e => e.Type == MatchEventType.Goal || e.Type == MatchEventType.PenaltyGoal)
                                          == m.HomeScore + m.AwayScore);

        await FluentActions.Awaiting(() => demo.SeedAsync()).Should().ThrowAsync<BusinessRuleException>();

        await demo.PurgeAsync();
        (await demo.CountAsync()).Should().Be(new DemoCounts(0, 0, 0, 0));
        (await s.Db.Clubs.AnyAsync(c => c.Id == realClubs[0])).Should().BeTrue();
    }
}
