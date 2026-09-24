using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Engagement;

public record PredictionVm(int HomeScore, int AwayScore, int? Points);

/// <summary>Répartition des pronostics d'un match (pourcentages domicile / nul / extérieur).</summary>
public record PredictionSummary(int Count, int HomePct, int DrawPct, int AwayPct);

public record MatchPredictionBlock(bool Open, PredictionVm? Mine, PredictionSummary Summary);

public record LeaderRow(int Rank, string UserId, string Name, int Points, int Exact, int Played);

public record Leaderboard(IReadOnlyList<LeaderRow> Top, LeaderRow? Me);

public record UpcomingPrediction(MatchRowVm Match, PredictionVm? Mine);

/// <summary>
/// Pronostics de score : ouverts jusqu'au coup d'envoi. Score exact = 3 points, bon vainqueur (ou nul) = 1 point.
/// En élimination directe, c'est le score à la fin du jeu qui compte (tirs au but non pris en compte).
/// </summary>
public class PredictionService(IAppDbContext db, IUserDirectory directory)
{
    public const int ExactPoints = 3;
    public const int OutcomePoints = 1;

    public static int Score(int predHome, int predAway, int home, int away) =>
        predHome == home && predAway == away ? ExactPoints
        : Math.Sign(predHome - predAway) == Math.Sign(home - away) ? OutcomePoints : 0;

    public static bool IsOpen(Match m, DateTimeOffset now) =>
        m.Status == MatchStatus.Scheduled && m.KickoffAt is { } k && k > now && m.HomeClubId is not null && m.AwayClubId is not null;

    public async Task<MatchPredictionBlock?> BlockAsync(int matchId, string? userId, CancellationToken ct = default)
    {
        var m = await db.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId, ct);
        if (m is null || m.HomeClubId is null || m.AwayClubId is null) return null;
        var open = IsOpen(m, DateTimeOffset.UtcNow);
        // Rien à montrer sur un match passé sans aucun pronostic.
        var rows = await db.Predictions.AsNoTracking().Where(p => p.MatchId == matchId)
            .Select(p => new { p.UserId, p.HomeScore, p.AwayScore, p.Points }).ToListAsync(ct);
        if (!open && rows.Count == 0) return null;
        var mine = userId is null ? null : rows.FirstOrDefault(r => r.UserId == userId);
        int Pct(int n) => rows.Count == 0 ? 0 : (int)Math.Round(100.0 * n / rows.Count);
        var summary = new PredictionSummary(rows.Count,
            Pct(rows.Count(r => r.HomeScore > r.AwayScore)), Pct(rows.Count(r => r.HomeScore == r.AwayScore)),
            Pct(rows.Count(r => r.HomeScore < r.AwayScore)));
        return new MatchPredictionBlock(open, mine is null ? null : new PredictionVm(mine.HomeScore, mine.AwayScore, mine.Points), summary);
    }

    public async Task SaveAsync(int matchId, string userId, int home, int away, CancellationToken ct = default)
    {
        if (home is < 0 or > 20 || away is < 0 or > 20) throw new BusinessRuleException("Score entre 0 et 20.");
        var m = await db.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId, ct) ?? throw new NotFoundException("Match");
        if (!IsOpen(m, DateTimeOffset.UtcNow)) throw new BusinessRuleException("Les pronostics sont fermés pour ce match.");
        var p = await db.Predictions.FirstOrDefaultAsync(x => x.MatchId == matchId && x.UserId == userId, ct);
        if (p is null) db.Predictions.Add(p = new Prediction { MatchId = matchId, UserId = userId });
        p.HomeScore = home;
        p.AwayScore = away;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Attribue (ou retire) les points après la fin (ou l'effacement) d'un résultat.</summary>
    public async Task ScoreMatchAsync(int matchId, CancellationToken ct = default)
    {
        var m = await db.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == matchId, ct);
        if (m is null) return;
        var counted = m.Status is MatchStatus.Finished or MatchStatus.Forfeit or MatchStatus.UnderReview
                      && m.HomeScore is not null && m.AwayScore is not null;
        var predictions = await db.Predictions.Where(p => p.MatchId == matchId).ToListAsync(ct);
        foreach (var p in predictions)
            p.Points = counted ? Score(p.HomeScore, p.AwayScore, m.HomeScore!.Value, m.AwayScore!.Value) : null;
        if (predictions.Count > 0) await db.SaveChangesAsync(ct);
    }

    public async Task<Leaderboard> LeaderboardAsync(int seasonId, string? userId, int take = 50, CancellationToken ct = default)
    {
        var rows = await db.Predictions.AsNoTracking()
            .Where(p => p.Points != null && p.Match.Phase.Competition.SeasonId == seasonId)
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Points = g.Sum(p => p.Points!.Value), Exact = g.Count(p => p.Points == ExactPoints), Played = g.Count() })
            .ToListAsync(ct);
        var ordered = rows.OrderByDescending(r => r.Points).ThenByDescending(r => r.Exact).ThenBy(r => r.Played).ToList();
        var ranked = new List<(int Rank, string UserId, int Points, int Exact, int Played)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            // Ex aequo : même rang (points et scores exacts identiques).
            var rank = i > 0 && ordered[i - 1].Points == r.Points && ordered[i - 1].Exact == r.Exact ? ranked[i - 1].Rank : i + 1;
            ranked.Add((rank, r.UserId, r.Points, r.Exact, r.Played));
        }
        var shown = ranked.Take(take).ToList();
        var meIndex = userId is null ? -1 : ranked.FindIndex(r => r.UserId == userId);
        var names = await directory.DisplayNamesAsync(shown.Select(r => r.UserId).Append(userId ?? "").Where(s => s.Length > 0), ct);
        LeaderRow Row((int Rank, string UserId, int Points, int Exact, int Played) r) =>
            new(r.Rank, r.UserId, names.GetValueOrDefault(r.UserId, "Supporter"), r.Points, r.Exact, r.Played);
        return new Leaderboard(shown.Select(Row).ToList(), meIndex >= 0 ? Row(ranked[meIndex]) : null);
    }

    /// <summary>Matchs des 7 prochains jours ouverts aux pronostics, avec le pronostic de l'utilisateur.</summary>
    public async Task<List<UpcomingPrediction>> UpcomingAsync(int seasonId, string? userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var until = now.AddDays(7);
        var matches = await db.Matches
            .Where(m => m.Phase.Competition.SeasonId == seasonId && m.Phase.Competition.IsPublished && m.Status == MatchStatus.Scheduled
                && m.KickoffAt > now && m.KickoffAt < until && m.HomeClubId != null && m.AwayClubId != null)
            .WithRowData().OrderBy(m => m.KickoffAt).Take(30).ToListAsync(ct);
        var ids = matches.Select(m => m.Id).ToList();
        var mine = userId is null ? [] : await db.Predictions.AsNoTracking().Where(p => p.UserId == userId && ids.Contains(p.MatchId))
            .ToDictionaryAsync(p => p.MatchId, p => new PredictionVm(p.HomeScore, p.AwayScore, p.Points), ct);
        return matches.Select(m => new UpcomingPrediction(Mapping.Row(m, now), mine.GetValueOrDefault(m.Id))).ToList();
    }

    /// <summary>Derniers pronostics notés de l'utilisateur (page « Mon compte »).</summary>
    public async Task<List<(MatchRowVm Match, PredictionVm Prediction)>> HistoryAsync(string userId, int take = 10, CancellationToken ct = default)
    {
        var preds = await db.Predictions.AsNoTracking().Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Match.KickoffAt).Take(take).ToListAsync(ct);
        var ids = preds.Select(p => p.MatchId).ToList();
        var now = DateTimeOffset.UtcNow;
        var rows = (await db.Matches.Where(m => ids.Contains(m.Id)).WithRowData().ToListAsync(ct)).ToDictionary(m => m.Id, m => Mapping.Row(m, now));
        return preds.Where(p => rows.ContainsKey(p.MatchId))
            .Select(p => (rows[p.MatchId], new PredictionVm(p.HomeScore, p.AwayScore, p.Points))).ToList();
    }
}
