using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

/// <summary>
/// Données de démonstration clairement FICTIVES (« ASC Démo 1 », « Joueur 7 Démo 3 »…), toutes marquées IsDemo
/// et supprimables en un clic. Elles servent à voir l'application remplie avant la vraie saison.
/// </summary>
public class DemoDataService(IAppDbContext db)
{
    private static readonly (string Primary, string Secondary)[] Colors =
    [
        ("#0E6B3A", "#FFFFFF"), ("#C62828", "#FFD54F"), ("#1A237E", "#FFFFFF"), ("#F9A825", "#1B1B1B"),
        ("#6A1B9A", "#FFFFFF"), ("#00838F", "#FFFFFF"), ("#FFFFFF", "#111111"), ("#4E342E", "#FFCC80"),
        ("#2E7D32", "#FFEB3B"), ("#AD1457", "#FFFFFF"), ("#263238", "#4FC3F7"), ("#EF6C00", "#FFFFFF"),
        ("#1565C0", "#FFEB3B"), ("#558B2F", "#FFFFFF"), ("#B71C1C", "#FFFFFF"), ("#37474F", "#FFAB00"),
        ("#00695C", "#FFFFFF"),
    ];

    /// <summary>Poules des zonales (4 ou 5 équipes, comme à Nguékokh) : Zone 5A = 4 + 4, Zone 5B = 5 + 4.</summary>
    private static readonly int[][] PoolSizes = [[4, 4], [5, 4]];

    public Task<bool> ExistsAsync(CancellationToken ct = default) =>
        db.Clubs.AnyAsync(c => c.IsDemo, ct);

    public async Task<DemoCounts> CountAsync(CancellationToken ct = default) => new(
        await db.Seasons.CountAsync(s => s.IsDemo, ct),
        await db.Clubs.CountAsync(c => c.IsDemo, ct),
        await db.Players.CountAsync(p => p.IsDemo, ct),
        await db.Matches.CountAsync(m => m.IsDemo, ct));

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await ExistsAsync(ct))
            throw new BusinessRuleException("Les données de démonstration sont déjà présentes.");
        var year = KokoraTime.Today.Year;
        if (await db.Seasons.AnyAsync(s => s.Year == year, ct))
            throw new BusinessRuleException($"Une saison {year} existe déjà : la démonstration n'est disponible que tant que la vraie saison n'est pas créée.");

        var rng = new Random(2026);
        var season = new Season { Year = year, Name = $"Saison {year} (démo)", IsCurrent = !await db.Seasons.AnyAsync(s => s.IsCurrent, ct), IsDemo = true };
        db.Seasons.Add(season);

        var stadiums = new[] { "Stade Démo A", "Stade Démo B" }
            .Select(n => new Stadium { Name = n, Neighborhood = "Quartier Démo", IsDemo = true }).ToList();
        db.Stadiums.AddRange(stadiums);
        var referees = Enumerable.Range(1, 3).Select(i => new Referee { FullName = $"Arbitre Démo {i}", IsDemo = true }).ToList();
        db.Referees.AddRange(referees);

        var clubs = new List<Club>();
        var perZone = PoolSizes.Select(z => z.Sum()).ToArray();
        for (var i = 1; i <= perZone.Sum(); i++)
        {
            var (primary, secondary) = Colors[i - 1];
            clubs.Add(new Club
            {
                Name = $"ASC Démo {i}", ShortName = $"Démo {i}", Slug = $"asc-demo-{i}",
                PrimaryColor = primary, SecondaryColor = secondary, Neighborhood = $"Quartier Démo {i}",
                Zone = i <= perZone[0] ? "5A" : "5B", ManagerName = $"Responsable Démo {i}", IsDemo = true
            });
        }
        db.Clubs.AddRange(clubs);

        var squads = new Dictionary<Club, List<Player>>();
        PlayerPosition[] positions = [PlayerPosition.Goalkeeper, .. Enumerable.Repeat(PlayerPosition.Defender, 5),
            .. Enumerable.Repeat(PlayerPosition.Midfielder, 5), .. Enumerable.Repeat(PlayerPosition.Forward, 3)];
        foreach (var (club, ci) in clubs.Select((c, i) => (c, i + 1)))
        {
            var players = new List<Player>();
            for (var n = 1; n <= positions.Length; n++)
            {
                var p = new Player
                {
                    FirstName = $"Joueur {n}", LastName = $"Démo {ci}", Slug = $"joueur-{n}-demo-{ci}",
                    Position = positions[n - 1], IsDemo = true
                };
                players.Add(p);
                db.SquadMembers.Add(new SquadMember { Season = season, Club = club, Player = p, ShirtNumber = n, IsCaptain = n == 4, IsDemo = true });
            }
            db.Players.AddRange(players);
            squads[club] = players;
        }

        // Structure : Zonales 5A et 5B en poules, puis 4 Grandes de chaque zone et Coupe du Maire vides.
        var comps = new List<Competition>();
        var specs = new (string Name, string Short, string Color)[]
        {
            ("Zonale 5A", "5A", "#0E6B3A"), ("Zonale 5B", "5B", "#B5471B"),
            ("4 Grandes Zone 5A", "4G 5A", "#14213D"), ("4 Grandes Zone 5B", "4G 5B", "#5B3A8C"), ("Coupe du Maire", "Coupe", "#9A7A2E")
        };
        for (var i = 0; i < specs.Length; i++)
            comps.Add(new Competition
            {
                Season = season, Name = specs[i].Name, ShortName = specs[i].Short, Slug = Slug.From(specs[i].Name),
                Color = specs[i].Color, Order = i + 1, IsDemo = true
            });
        db.Competitions.AddRange(comps);

        var firstMatchday = KokoraTime.Today.AddDays(-14);
        for (var z = 0; z < 2; z++)
        {
            var comp = comps[z];
            var pools = new Phase { Competition = comp, Name = "Phase de poules", Type = PhaseType.League, Order = 1, IsDemo = true };
            db.Phases.Add(pools);
            db.Phases.Add(new Phase { Competition = comp, Name = "Phase finale", Type = PhaseType.Knockout, Order = 2, IsDemo = true });

            var offset = perZone.Take(z).Sum();
            for (var g = 0; g < PoolSizes[z].Length; g++)
            {
                var teams = clubs.Skip(offset + PoolSizes[z].Take(g).Sum()).Take(PoolSizes[z][g]).ToList();
                var group = new Group
                {
                    Phase = pools, Name = g == 0 ? "Poule A" : "Poule B", Order = g + 1, IsDemo = true,
                    Zones = [new StandingZone { FromRank = 1, ToRank = 2, Kind = StandingZoneKind.Qualified, Label = "Phase finale" }]
                };
                for (var s = 0; s < teams.Count; s++) group.Teams.Add(new GroupTeam { Club = teams[s], Seed = s + 1 });
                db.Groups.Add(group);

                // Ids pas encore connus : on génère le calendrier sur les index puis on remappe.
                var fixtures = RoundRobin.Generate(Enumerable.Range(0, teams.Count).ToList(), homeAndAway: true);
                foreach (var dayFixtures in fixtures.GroupBy(f => f.Matchday))
                {
                    var slot = 0;
                    foreach (var f in dayFixtures)
                    {
                        var date = firstMatchday.AddDays((f.Matchday - 1) * 3 + z);
                        var hour = g == 0 ? 16 : 17;
                        var kickoff = ScheduleService.ToUtc(date.ToDateTime(new TimeOnly(hour, 0).AddMinutes(120 * slot)));
                        var home = teams[f.HomeId];
                        var away = teams[f.AwayId];
                        var match = new Match
                        {
                            Phase = pools, Group = group, Matchday = f.Matchday, HomeClub = home, AwayClub = away,
                            KickoffAt = kickoff, Stadium = stadiums[(g + slot) % 2], Referee = referees[(f.Matchday + slot) % 3],
                            IsDemo = true
                        };
                        if (kickoff < DateTimeOffset.UtcNow.AddHours(-2))
                            PlayMatch(match, squads[home], squads[away], rng);
                        db.Matches.Add(match);
                        slot++;
                    }
                }
            }
        }

        // 4 Grandes Zone 5A : demi-finales jouées (dont une aux tirs au but), finale à l'affiche dans 6 jours.
        var grandes = new Phase { Competition = comps[2], Name = "Demi-finales et finale", Type = PhaseType.Knockout, Order = 1, IsDemo = true };
        db.Phases.Add(grandes);
        var semiRound = new Round { Phase = grandes, Name = "Demi-finales", Kind = RoundKind.SemiFinal, Order = 1, IsDemo = true };
        var finalRound = new Round { Phase = grandes, Name = "Finale", Kind = RoundKind.Final, Order = 2, IsDemo = true };
        db.Rounds.AddRange(semiRound, finalRound);
        var semiDay = KokoraTime.Today.AddDays(-1);
        Match Semi(int pos, Club home, Club away, int hs, int aws, int? hp = null, int? ap = null)
        {
            var m = new Match
            {
                Phase = grandes, Round = semiRound, BracketPosition = pos, HomeClub = home, AwayClub = away,
                KickoffAt = ScheduleService.ToUtc(semiDay.ToDateTime(new TimeOnly(15 + pos * 2, 0))), Stadium = stadiums[0],
                Referee = referees[pos], IsDemo = true
            };
            PlayMatch(m, squads[home], squads[away], rng, hs, aws);
            m.HomePenalties = hp;
            m.AwayPenalties = ap;
            db.Matches.Add(m);
            return m;
        }
        Semi(1, clubs[0], clubs[5], 1, 1, 4, 3);
        Semi(2, clubs[4], clubs[1], 2, 0);
        db.Matches.Add(new Match
        {
            Phase = grandes, Round = finalRound, BracketPosition = 1, HomeClub = clubs[0], AwayClub = clubs[4],
            HomePlaceholder = "Vainqueur DF1", AwayPlaceholder = "Vainqueur DF2",
            KickoffAt = ScheduleService.ToUtc(KokoraTime.Today.AddDays(6).ToDateTime(new TimeOnly(17, 0))),
            Stadium = stadiums[0], Referee = referees[0], IsFeatured = true, IsDemo = true
        });
        foreach (var c in new[] { comps[3], comps[4] })
            db.Phases.Add(new Phase { Competition = c, Name = "Tableau final", Type = PhaseType.Knockout, Order = 1, IsDemo = true });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Simule un résultat et ses événements (buteurs, passeurs, cartons) de façon reproductible.</summary>
    private static void PlayMatch(Match m, List<Player> home, List<Player> away, Random rng, int? homeGoals = null, int? awayGoals = null)
    {
        int Goals() => rng.Next(100) switch { < 25 => 0, < 55 => 1, < 80 => 2, < 93 => 3, _ => 4 };
        var hs = homeGoals ?? Goals();
        var aws = awayGoals ?? Goals();
        m.Status = MatchStatus.Finished;
        m.LivePeriod = LivePeriod.Ended;
        m.HomeScore = hs;
        m.AwayScore = aws;

        var minutes = new List<(int Minute, bool Home)>();
        for (var i = 0; i < hs; i++) minutes.Add((rng.Next(3, 90), true));
        for (var i = 0; i < aws; i++) minutes.Add((rng.Next(3, 90), false));
        m.HomeHalfTimeScore = minutes.Count(x => x.Home && x.Minute <= 45);
        m.AwayHalfTimeScore = minutes.Count(x => !x.Home && x.Minute <= 45);

        foreach (var (minute, isHome) in minutes.OrderBy(x => x.Minute))
        {
            var squad = isHome ? home : away;
            // Les attaquants et milieux marquent plus souvent.
            var scorer = squad[rng.Next(100) < 70 ? rng.Next(9, 14) : rng.Next(1, 14)];
            Player? assist = rng.Next(100) < 65 ? squad[rng.Next(5, 14)] : null;
            if (assist == scorer) assist = null;
            m.Events.Add(new MatchEvent
            {
                Type = rng.Next(100) < 8 ? MatchEventType.PenaltyGoal : MatchEventType.Goal,
                Period = minute <= 45 ? LivePeriod.FirstHalf : LivePeriod.SecondHalf, Minute = minute,
                Club = isHome ? m.HomeClub : m.AwayClub, Player = scorer,
                AssistPlayer = assist, IsDemo = true
            });
        }

        var cards = rng.Next(0, 4);
        for (var i = 0; i < cards; i++)
        {
            var isHome = rng.Next(2) == 0;
            var squad = isHome ? home : away;
            var minute = rng.Next(10, 90);
            m.Events.Add(new MatchEvent
            {
                Type = rng.Next(100) < 90 ? MatchEventType.YellowCard : MatchEventType.RedCard,
                Period = minute <= 45 ? LivePeriod.FirstHalf : LivePeriod.SecondHalf, Minute = minute,
                Club = isHome ? m.HomeClub : m.AwayClub, Player = squad[rng.Next(1, 14)], IsDemo = true
            });
        }
    }

    /// <summary>Supprime toutes les données marquées démo (dans l'ordre imposé par les clés étrangères).</summary>
    public async Task PurgeAsync(CancellationToken ct = default)
    {
        await db.MatchEvents.Where(e => e.IsDemo || e.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.LineupEntries.Where(l => l.IsDemo || l.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.Predictions.Where(p => p.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.ManOfTheMatchVotes.Where(v => v.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.Qualifications.Where(q => q.IsDemo || q.TargetPhase.IsDemo).ExecuteDeleteAsync(ct);
        await db.Protests.Where(p => p.IsDemo || p.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.Suspensions.Where(s => s.IsDemo || s.Player.IsDemo).ExecuteDeleteAsync(ct);
        await db.Photos.Where(p => p.IsDemo).ExecuteDeleteAsync(ct);
        await db.Matches.Where(m => m.IsDemo).ExecuteDeleteAsync(ct);
        await db.PointAdjustments.Where(p => p.IsDemo || p.Group.IsDemo).ExecuteDeleteAsync(ct);
        await db.GroupTeams.Where(t => t.Group.IsDemo || t.Club.IsDemo).ExecuteDeleteAsync(ct);
        await db.Groups.Where(g => g.IsDemo).ExecuteDeleteAsync(ct);
        await db.Rounds.Where(r => r.IsDemo).ExecuteDeleteAsync(ct);
        await db.Phases.Where(p => p.IsDemo).ExecuteDeleteAsync(ct);
        await db.Competitions.Where(c => c.IsDemo).ExecuteDeleteAsync(ct);
        await db.SquadMembers.Where(s => s.IsDemo || s.Player.IsDemo || s.Club.IsDemo).ExecuteDeleteAsync(ct);
        await db.Seasons.Where(s => s.IsDemo).ExecuteDeleteAsync(ct);
        await db.FavoriteClubs.Where(f => f.Club.IsDemo).ExecuteDeleteAsync(ct);
        await db.Players.Where(p => p.IsDemo).ExecuteDeleteAsync(ct);
        await db.Clubs.Where(c => c.IsDemo).ExecuteDeleteAsync(ct);
        await db.Stadiums.Where(s => s.IsDemo).ExecuteDeleteAsync(ct);
        await db.Referees.Where(r => r.IsDemo).ExecuteDeleteAsync(ct);

        // Si la saison courante était la démo, la plus récente redevient courante.
        if (!await db.Seasons.AnyAsync(s => s.IsCurrent, ct)
            && await db.Seasons.OrderByDescending(s => s.Year).FirstOrDefaultAsync(ct) is { } latest)
        {
            latest.IsCurrent = true;
            await db.SaveChangesAsync(ct);
        }
    }
}

public record DemoCounts(int Seasons, int Clubs, int Players, int Matches);
