using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Content;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

/// <summary>
/// Données de démonstration clairement FICTIVES (« ASC Démo 1 », « Joueur 7 Démo 3 »…), toutes marquées IsDemo
/// et supprimables en un clic. Elles servent à voir l'application remplie avant la vraie saison.
/// </summary>
public class DemoDataService(IAppDbContext db, CompetitionCache cache)
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
        await SeedArticlesAsync(clubs, ct);
        await AddFictitiousVotesAsync(season.Id, rng, ct);
    }

    /// <summary>Quelques infos fictives (titres marqués « démo ») pour voir la page Infos remplie.</summary>
    private async Task SeedArticlesAsync(List<Club> clubs, CancellationToken ct)
    {
        async Task<ArticleCategory> Category(string name)
        {
            var slug = Slug.From(name);
            var cat = await db.ArticleCategories.FirstOrDefaultAsync(c => c.Slug == slug, ct);
            if (cat is not null) return cat;
            cat = new ArticleCategory { Name = name, Slug = slug, Order = 90, IsDemo = true };
            db.ArticleCategories.Add(cat);
            return cat;
        }
        var final = await db.Matches.Include(m => m.Round).FirstAsync(m => m.IsDemo && m.Round != null && m.Round.Kind == RoundKind.Final, ct);
        var semi = await db.Matches.Include(m => m.Round)
            .FirstAsync(m => m.IsDemo && m.Round != null && m.Round.Kind == RoundKind.SemiFinal && m.HomePenalties != null, ct);
        var tagFinale = new Tag { Name = "Finale (démo)", Slug = "finale-demo", IsDemo = true };
        var tagZone = new Tag { Name = "Zone 5A (démo)", Slug = "zone-5a-demo", IsDemo = true };
        var now = DateTimeOffset.UtcNow;
        const string note = "<p><em>Texte fictif de démonstration : il sera supprimé avec les autres données de démo.</em></p>";

        Article Make(string title, string category, string body, int hoursAgo, string? summary = null) => new()
        {
            Title = title, Slug = Slug.From(title, 100), Summary = summary, Body = body + note,
            Status = ArticleStatus.Published, PublishedAt = now.AddHours(-hoursAgo), AuthorName = "Rédaction (démo)", IsDemo = true
        };

        var communique = Make("Finale des 4 Grandes Zone 5A : le programme (démo)", "Communiqués",
            "<p>La finale des 4 Grandes de la Zone 5A opposera <strong>ASC Démo 1</strong> à <strong>ASC Démo 5</strong>.</p>" +
            "<h2>Organisation</h2><ul><li>Ouverture des portes deux heures avant le coup d'envoi.</li>" +
            "<li>Les supporters sont invités à respecter les consignes des organisateurs.</li></ul>" +
            "<blockquote>Le fair-play reste la priorité de la commission d'organisation.</blockquote>", 3,
            "Horaires, accès au stade et consignes pour la finale de dimanche.");
        communique.Category = await Category("Communiqués");
        communique.IsFeatured = true;
        communique.IsImportant = true;
        communique.Clubs = [clubs[0], clubs[4]];
        communique.Matches = [final];
        communique.Tags = [tagFinale, tagZone];

        var resume = Make("ASC Démo 1 passe aux tirs au but (démo)", "Résumés de matchs",
            "<p>Au terme d'une demi-finale disputée, <strong>ASC Démo 1</strong> a eu besoin des tirs au but pour écarter " +
            "<strong>ASC Démo 6</strong> (1-1, 4 tirs au but à 3).</p><p>Le gardien a arrêté la dernière tentative adverse.</p>", 30);
        resume.Category = await Category("Résumés de matchs");
        resume.Clubs = [clubs[0], clubs[5]];
        resume.Matches = [semi];
        resume.Tags = [tagZone];

        var commission = Make("Décisions de la commission de discipline (démo)", "Commission",
            "<p>Réunie en séance ordinaire, la commission a examiné les rapports des arbitres de la dernière journée.</p>" +
            "<ol><li>Un match de suspension pour un joueur exclu.</li><li>Avertissement adressé à une équipe pour retard.</li></ol>", 52);
        commission.Category = await Category("Commission");

        var portrait = Make("Portrait : le capitaine d'ASC Démo 3 (démo)", "Portraits",
            "<p>Portrait fictif d'un capitaine, utilisé pour montrer la mise en page d'un article plus long.</p>" +
            string.Concat(Enumerable.Range(1, 4).Select(i => $"<p>Paragraphe de démonstration n°{i}. Le texte d'un article s'affiche " +
                "sur une colonne de lecture confortable, avec des intertitres, des listes et des citations.</p>")), 96);
        portrait.Category = await Category("Portraits");
        portrait.Clubs = [clubs[2]];

        var draft = Make("Présentation des arbitres de la saison (brouillon démo)", "Annonces", "<p>Brouillon non publié.</p>", 0);
        draft.Status = ArticleStatus.Draft;
        draft.PublishedAt = null;
        var scheduled = Make("Programmée : ouverture des inscriptions (démo)", "Annonces", "<p>Info programmée pour plus tard.</p>", 0);
        scheduled.Status = ArticleStatus.Scheduled;
        scheduled.PublishedAt = now.AddDays(2);
        scheduled.Category = draft.Category = await Category("Annonces");

        db.Articles.AddRange(communique, resume, commission, portrait, draft, scheduled);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Complète une saison réelle (équipes et poules saisies ou importées) avec des données FICTIVES marquées démo :
    /// effectifs « Joueur 1 … 14 », calendrier des poules sans matchs (une journée par semaine, la première il y a 3 semaines)
    /// et résultats simulés des matchs passés. « Supprimer les données de démo » les retire en gardant équipes et poules.
    /// </summary>
    public async Task<FillReport> FillSeasonAsync(int seasonId, CancellationToken ct = default)
    {
        if (!await db.Seasons.AnyAsync(s => s.Id == seasonId, ct)) throw new NotFoundException("Saison");
        var groups = await db.Groups.Include(g => g.Teams).ThenInclude(t => t.Club).Include(g => g.Phase).ThenInclude(p => p.Competition)
            .Where(g => g.Phase.Competition.SeasonId == seasonId && g.Phase.Type == PhaseType.League && g.Teams.Count >= 2)
            .OrderBy(g => g.Phase.Competition.Order).ThenBy(g => g.Order).ThenBy(g => g.Name)
            .ToListAsync(ct);
        if (groups.Count == 0)
            throw new BusinessRuleException("Aucune poule dans cette saison : importez d'abord le tirage (ou créez les poules).");

        var rng = new Random(seasonId * 31 + 7);
        var clubs = groups.SelectMany(g => g.Teams).Select(t => t.Club).DistinctBy(c => c.Id).ToList();
        var clubIds = clubs.Select(c => c.Id).ToList();
        var existing = await db.SquadMembers.Include(s => s.Player)
            .Where(s => s.SeasonId == seasonId && clubIds.Contains(s.ClubId)).ToListAsync(ct);
        var squads = existing.GroupBy(s => s.ClubId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.ShirtNumber ?? 99).Select(s => s.Player).ToList());

        PlayerPosition[] positions = [PlayerPosition.Goalkeeper, .. Enumerable.Repeat(PlayerPosition.Defender, 5),
            .. Enumerable.Repeat(PlayerPosition.Midfielder, 5), .. Enumerable.Repeat(PlayerPosition.Forward, 3)];
        var playersCreated = 0;
        foreach (var club in clubs.Where(c => !squads.ContainsKey(c.Id)))
        {
            var players = new List<Player>();
            for (var n = 1; n <= positions.Length; n++)
            {
                var p = new Player
                {
                    FirstName = $"Joueur {n}", LastName = club.ShortName, Slug = $"joueur-{n}-{club.Slug}-fictif",
                    Position = positions[n - 1], IsDemo = true
                };
                players.Add(p);
                db.SquadMembers.Add(new SquadMember { SeasonId = seasonId, Club = club, Player = p, ShirtNumber = n, IsCaptain = n == 4, IsDemo = true });
            }
            db.Players.AddRange(players);
            squads[club.Id] = players;
            playersCreated += players.Count;
        }

        var withMatches = await db.Matches.Where(m => m.GroupId != null && groups.Select(g => g.Id).Contains(m.GroupId.Value))
            .Select(m => m.GroupId!.Value).Distinct().ToListAsync(ct);
        var firstDay = KokoraTime.Today.AddDays(-21);
        var now = DateTimeOffset.UtcNow;
        int matchesCreated = 0, played = 0;
        foreach (var (group, gi) in groups.Select((g, i) => (g, i)))
        {
            if (withMatches.Contains(group.Id)) continue;
            var byId = group.Teams.ToDictionary(t => t.ClubId, t => t.Club);
            var fixtures = RoundRobin.Generate(group.Teams.OrderBy(t => t.Seed).Select(t => t.ClubId).ToList(), homeAndAway: false);
            foreach (var day in fixtures.GroupBy(f => f.Matchday))
            {
                // Poules réparties sur trois jours de la semaine ; deux horaires par jour.
                var date = firstDay.AddDays(7 * (day.Key - 1) + gi % 3);
                foreach (var (f, slot) in day.Select((f, i) => (f, i)))
                {
                    var m = new Match
                    {
                        PhaseId = group.PhaseId, GroupId = group.Id, Matchday = f.Matchday,
                        HomeClub = byId[f.HomeId], AwayClub = byId[f.AwayId],
                        KickoffAt = ScheduleService.ToUtc(date.ToDateTime(slot % 2 == 0 ? new TimeOnly(16, 0) : new TimeOnly(17, 45))),
                        IsDemo = true
                    };
                    if (m.KickoffAt < now.AddHours(-2))
                    {
                        if (squads[f.HomeId].Count >= 14 && squads[f.AwayId].Count >= 14)
                            PlayMatch(m, squads[f.HomeId], squads[f.AwayId], rng);
                        else
                        {
                            (m.HomeScore, m.AwayScore) = (rng.Next(0, 4), rng.Next(0, 3));
                            m.Status = MatchStatus.Finished;
                            m.LivePeriod = LivePeriod.Ended;
                        }
                        played++;
                    }
                    db.Matches.Add(m);
                    matchesCreated++;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        await AddFictitiousVotesAsync(seasonId, rng, ct);
        foreach (var compId in groups.Select(g => g.Phase.CompetitionId).Distinct()) cache.Invalidate(compId);
        return new FillReport(playersCreated, matchesCreated, played);
    }

    /// <summary>
    /// Votes « homme du match » fictifs (comptes « fictif-N », sans compte réel) sur les matchs de démo joués :
    /// les buteurs et passeurs reçoivent plus de voix. Supprimés avec les matchs de démo.
    /// </summary>
    public async Task<int> AddFictitiousVotesAsync(int seasonId, Random? random = null, CancellationToken ct = default)
    {
        var rng = random ?? new Random(seasonId * 17 + 3);
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.IsDemo && m.Phase.Competition.SeasonId == seasonId && m.Status == MatchStatus.Finished
                && m.HomeClubId != null && m.AwayClubId != null && !db.ManOfTheMatchVotes.Any(v => v.MatchId == m.Id))
            .Select(m => new
            {
                m.Id, Home = m.HomeClubId!.Value, Away = m.AwayClubId!.Value,
                Goals = m.Events.Where(e => !e.IsCancelled && e.PlayerId != null && (e.Type == MatchEventType.Goal || e.Type == MatchEventType.PenaltyGoal))
                    .Select(e => new { e.PlayerId, e.AssistPlayerId }).ToList()
            })
            .ToListAsync(ct);
        var squads = (await db.SquadMembers.AsNoTracking().Where(s => s.SeasonId == seasonId).Select(s => new { s.ClubId, s.PlayerId }).ToListAsync(ct))
            .GroupBy(s => s.ClubId).ToDictionary(g => g.Key, g => g.Select(s => s.PlayerId).ToList());
        var total = 0;
        foreach (var m in matches)
        {
            var pool = squads.GetValueOrDefault(m.Home, []).Concat(squads.GetValueOrDefault(m.Away, [])).ToList();
            var decisive = m.Goals.SelectMany(g => new[] { g.PlayerId, g.AssistPlayerId }).OfType<int>().Where(pool.Contains).ToList();
            if (pool.Count == 0) continue;
            var count = rng.Next(4, 16);
            for (var i = 0; i < count; i++)
            {
                var playerId = decisive.Count > 0 && rng.Next(100) < 70 ? decisive[rng.Next(decisive.Count)] : pool[rng.Next(pool.Count)];
                db.ManOfTheMatchVotes.Add(new Kokora.Domain.Users.ManOfTheMatchVote { MatchId = m.Id, UserId = $"fictif-{i + 1}", PlayerId = playerId });
            }
            total += count;
        }
        await db.SaveChangesAsync(ct);
        await DesignateFictitiousManOfTheMatchAsync(seasonId, rng, ct);
        return total;
    }

    /// <summary>Homme du match « officiel » fictif des matchs de démo joués : le joueur le plus décisif, sinon un joueur au hasard.</summary>
    public async Task<int> DesignateFictitiousManOfTheMatchAsync(int seasonId, Random rng, CancellationToken ct = default)
    {
        var matches = await db.Matches.Include(m => m.Events)
            .Where(m => m.IsDemo && m.Phase.Competition.SeasonId == seasonId && m.Status == MatchStatus.Finished && m.ManOfTheMatchPlayerId == null
                && m.HomeClubId != null && m.AwayClubId != null)
            .ToListAsync(ct);
        var squads = (await db.SquadMembers.AsNoTracking().Where(s => s.SeasonId == seasonId).Select(s => new { s.ClubId, s.PlayerId }).ToListAsync(ct))
            .GroupBy(s => s.ClubId).ToDictionary(g => g.Key, g => g.Select(s => s.PlayerId).ToList());
        foreach (var m in matches)
        {
            var decisive = m.Events.Where(e => !e.IsCancelled && e.PlayerId != null && e.Type is MatchEventType.Goal or MatchEventType.PenaltyGoal)
                .SelectMany(e => new[] { e.PlayerId, e.PlayerId, e.AssistPlayerId }).OfType<int>()
                .GroupBy(id => id).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            var pool = squads.GetValueOrDefault(m.HomeClubId!.Value, []).Concat(squads.GetValueOrDefault(m.AwayClubId!.Value, [])).ToList();
            m.ManOfTheMatchPlayerId = decisive != 0 ? decisive : pool.Count > 0 ? pool[rng.Next(pool.Count)] : null;
        }
        await db.SaveChangesAsync(ct);
        return matches.Count;
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
        await db.Comments.Where(c => c.Article.IsDemo).ExecuteDeleteAsync(ct);
        await db.Photos.Where(p => p.Article != null && p.Article.IsDemo).ExecuteDeleteAsync(ct);
        await db.Articles.Where(a => a.IsDemo).ExecuteDeleteAsync(ct);
        await db.Tags.Where(t => t.IsDemo).ExecuteDeleteAsync(ct);
        await db.ArticleCategories.Where(c => c.IsDemo).ExecuteDeleteAsync(ct);
        await db.MatchEvents.Where(e => e.IsDemo || e.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.LineupEntries.Where(l => l.IsDemo || l.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.Predictions.Where(p => p.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.ManOfTheMatchVotes.Where(v => v.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.Qualifications.Where(q => q.IsDemo || q.TargetPhase.IsDemo).ExecuteDeleteAsync(ct);
        await db.Protests.Where(p => p.IsDemo || p.Match.IsDemo).ExecuteDeleteAsync(ct);
        await db.Suspensions.Where(s => s.IsDemo || s.Player.IsDemo).ExecuteDeleteAsync(ct);
        await db.Photos.Where(p => p.IsDemo || (p.Match != null && p.Match.IsDemo)).ExecuteDeleteAsync(ct);
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

public record FillReport(int Players, int Matches, int Played);
