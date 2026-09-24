using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Public;

/// <summary>Équipes, joueurs et recherche (pages publiques).</summary>
public class DirectoryService(IAppDbContext db, StatsService stats, StandingsService standings, MatchQueryService matches)
{
    private static readonly MatchStatus[] Played = [MatchStatus.Finished, MatchStatus.Forfeit, MatchStatus.UnderReview, MatchStatus.Abandoned];

    /// <summary>Équipes actives, regroupées par zone.</summary>
    public async Task<List<(string Zone, List<(TeamVm Team, string? Neighborhood)> Teams)>> TeamsAsync(CancellationToken ct = default)
    {
        var clubs = await db.Clubs.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(ct);
        return clubs.GroupBy(c => c.Zone is null ? "Autres équipes" : $"Zone {c.Zone}").OrderBy(g => g.Key == "Autres équipes").ThenBy(g => g.Key)
            .Select(g => (g.Key, g.Select(c => (Mapping.Team(c)!, c.Neighborhood)).ToList())).ToList();
    }

    public async Task<TeamPageData?> TeamAsync(string slug, CancellationToken ct = default)
    {
        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct);
        if (club is null) return null;
        var team = Mapping.Team(club)!;
        var season = await matches.CurrentSeasonAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var squad = season is null ? [] : (await db.SquadMembers.AsNoTracking().Include(s => s.Player)
                .Where(s => s.SeasonId == season.Id && s.ClubId == club.Id).ToListAsync(ct))
            .OrderBy(s => s.Player.Position == PlayerPosition.Unknown).ThenBy(s => s.Player.Position).ThenBy(s => s.ShirtNumber ?? 99)
            .Select(s => StatsService.ToVm(s.Player, s.ShirtNumber)).ToList();

        var seasonMatches = season is null ? [] : await db.Matches
            .Where(m => m.Phase.Competition.SeasonId == season.Id && m.Phase.Competition.IsPublished
                && (m.HomeClubId == club.Id || m.AwayClubId == club.Id))
            .WithRowData().ToListAsync(ct);
        var rows = seasonMatches.Select(m => Mapping.Row(m, now)).ToList();
        var upcoming = rows.Where(r => r.Status is MatchStatus.Scheduled or MatchStatus.Postponed or MatchStatus.Live or MatchStatus.HalfTime)
            .OrderByDescending(r => r.IsLive).ThenBy(r => r.KickoffAt ?? DateTimeOffset.MaxValue).ToList();
        var results = rows.Where(r => Played.Contains(r.Status)).OrderByDescending(r => r.KickoffAt).ToList();

        // Forme : 5 derniers matchs comptabilisés, du plus ancien au plus récent.
        var form = results.Where(r => r.Status != MatchStatus.Abandoned).Take(5).Reverse()
            .Select(r => r.Outcome == 0 ? 'N' : (r.Outcome == 1) == (r.Home?.Id == club.Id) ? 'V' : 'D').ToList();

        // Classements des poules où joue l'équipe.
        var tables = new List<(string, string, StandingsTableVm)>();
        var compIds = seasonMatches.Select(m => m.Phase.CompetitionId).Distinct().ToList();
        var comps = await db.Competitions.AsNoTracking().Where(c => compIds.Contains(c.Id)).OrderBy(c => c.Order).ToListAsync(ct);
        foreach (var comp in comps)
            foreach (var phase in await standings.CompetitionAsync(comp.Id, ct))
                foreach (var g in phase.Groups.Where(g => g.Table.Rows.Any(r => r.Team.Id == club.Id)))
                    tables.Add(($"{comp.Name} · {g.Name}", $"/classements/{comp.Slug}?phase={phase.PhaseId}",
                        g.Table with { Favorites = new HashSet<int> { club.Id } }));

        var seasonStats = season is null ? null : await stats.GetAsync(season.Id, null, ct);
        var scorers = seasonStats?.Contributions.Where(r => r.Team?.Id == club.Id).Take(8).ToList() ?? [];
        var totals = (await stats.TeamRowsAsync(compIds, ct, club.Id)).FirstOrDefault();
        var suspended = seasonStats?.Suspended.Where(s => s.Team.Id == club.Id).ToList() ?? [];

        return new TeamPageData(team, club.Neighborhood, club.Zone, club.FoundedYear, season?.Name, squad,
            upcoming, results, tables, scorers, totals, suspended, form);
    }

    public async Task<PlayerPageData?> PlayerAsync(string slug, CancellationToken ct = default)
    {
        var player = await db.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (player is null) return null;
        var season = await matches.CurrentSeasonAsync(ct);
        var member = season is null ? null : await db.SquadMembers.AsNoTracking().Include(s => s.Club)
            .FirstOrDefaultAsync(s => s.PlayerId == player.Id && s.SeasonId == season.Id, ct);

        var events = await db.MatchEvents.AsNoTracking()
            .Where(e => !e.IsCancelled && (e.PlayerId == player.Id || e.AssistPlayerId == player.Id)
                && e.Match.Phase.Competition.IsPublished && StatsService.RealMatches.Contains(e.Match.Status))
            .Select(e => new { e.MatchId, e.Type, e.PlayerId, e.AssistPlayerId, Comp = e.Match.Phase.Competition.Name, CompOrder = e.Match.Phase.Competition.Order })
            .ToListAsync(ct);
        var lineupMatches = await db.LineupEntries.AsNoTracking().Where(l => l.PlayerId == player.Id)
            .Select(l => l.MatchId).ToListAsync(ct);
        var matchIds = events.Select(e => e.MatchId).Union(lineupMatches).Distinct().ToList();

        bool Scored(MatchEventType t) => t is MatchEventType.Goal or MatchEventType.PenaltyGoal;
        var own = events.Where(e => e.PlayerId == player.Id).ToList();
        var assists = events.Where(e => e.AssistPlayerId == player.Id && Scored(e.Type)).ToList();

        var now = DateTimeOffset.UtcNow;
        var matchRows = (await db.Matches.Where(m => matchIds.Contains(m.Id)).WithRowData().ToListAsync(ct))
            .OrderByDescending(m => m.KickoffAt)
            .Select(m => new PlayerMatchLine(Mapping.Row(m, now),
                own.Count(e => e.MatchId == m.Id && Scored(e.Type)),
                assists.Count(e => e.MatchId == m.Id),
                own.Any(e => e.MatchId == m.Id && e.Type == MatchEventType.YellowCard),
                own.Any(e => e.MatchId == m.Id && e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow)))
            .ToList();

        var byComp = own.Select(e => e.Comp).Union(assists.Select(e => e.Comp)).Distinct()
            .Select(c => (c,
                own.Count(e => e.Comp == c && Scored(e.Type)),
                assists.Count(e => e.Comp == c),
                own.Count(e => e.Comp == c && e.Type == MatchEventType.YellowCard),
                own.Count(e => e.Comp == c && e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow)))
            .ToList();

        var suspensions = season is null ? [] : (await stats.GetAsync(season.Id, null, ct)).Suspended.Where(s => s.Player.Id == player.Id).ToList();

        return new PlayerPageData(
            StatsService.ToVm(player, member?.ShirtNumber), player.Nickname, player.FullName,
            member is null ? null : Mapping.Team(member.Club), player.BirthDate, season?.Name,
            matchIds.Count,
            own.Count(e => Scored(e.Type)), own.Count(e => e.Type == MatchEventType.PenaltyGoal), assists.Count,
            own.Count(e => e.Type == MatchEventType.YellowCard), own.Count(e => e.Type is MatchEventType.RedCard or MatchEventType.SecondYellow),
            byComp, matchRows, suspensions);
    }

    /// <summary>Recherche insensible aux accents (via les slugs) : équipes puis joueurs.</summary>
    public async Task<SearchResults> SearchAsync(string? q, CancellationToken ct = default)
    {
        var text = q?.Trim() ?? "";
        var key = Slug.From(text);
        if (key.Length < 2) return new SearchResults([], []);
        var lower = text.ToLower();

        var clubs = await db.Clubs.AsNoTracking()
            .Where(c => c.Slug.Contains(key) || c.ShortName.ToLower().Contains(lower) || (c.Neighborhood ?? "").ToLower().Contains(lower))
            .OrderBy(c => !c.IsActive).ThenBy(c => c.Name).Take(8).ToListAsync(ct);

        var season = await matches.CurrentSeasonAsync(ct);
        var seasonId = season?.Id ?? 0;
        var players = await db.Players.AsNoTracking()
            .Where(p => p.Slug.Contains(key) || (p.Nickname ?? "").ToLower().Contains(lower))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(12)
            .Select(p => new { Player = p, Club = p.Memberships.Where(m => m.SeasonId == seasonId).Select(m => m.Club).FirstOrDefault(),
                Number = p.Memberships.Where(m => m.SeasonId == seasonId).Select(m => m.ShirtNumber).FirstOrDefault() })
            .ToListAsync(ct);

        return new SearchResults(
            clubs.Select(c => Mapping.Team(c)!).ToList(),
            players.Select(x => (StatsService.ToVm(x.Player, x.Number), Mapping.Team(x.Club))).ToList());
    }
}
