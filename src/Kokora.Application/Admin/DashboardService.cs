using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public record DashboardData(
    int? SeasonId, string? SeasonName,
    IReadOnlyList<AdminMatchItem> Today,
    IReadOnlyList<AdminMatchItem> MissingResults,
    int Clubs, int Players, int MatchesPlanned, int MatchesPlayed,
    IReadOnlyList<AuditLog> RecentChanges);

public record AuditFilter(string? EntityType = null, string? User = null, DateOnly? Day = null, int Page = 1, int PageSize = 50);

public class DashboardService(IAppDbContext db, ScheduleService schedule)
{
    public async Task<DashboardData> GetAsync(CancellationToken ct = default)
    {
        var season = await db.Seasons.AsNoTracking().OrderByDescending(s => s.IsCurrent).ThenByDescending(s => s.Year).FirstOrDefaultAsync(ct);
        var recent = await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.At).Take(8).ToListAsync(ct);
        if (season is null)
            return new DashboardData(null, null, [], [], 0, 0, 0, 0, recent);

        var today = await schedule.ListAsync(new MatchFilter(season.Id, Day: KokoraTime.Today), ct);
        var missing = await schedule.ListAsync(new MatchFilter(season.Id, OnlyMissingResult: true), ct);
        var matches = db.Matches.Where(m => m.Phase.Competition.SeasonId == season.Id);

        return new DashboardData(season.Id, season.Name, today, missing.OrderByDescending(m => m.KickoffAt).Take(10).ToList(),
            await db.Clubs.CountAsync(x => x.IsActive, ct),
            await db.SquadMembers.CountAsync(s => s.SeasonId == season.Id, ct),
            await matches.CountAsync(ct),
            await matches.CountAsync(m => m.Status == MatchStatus.Finished || m.Status == MatchStatus.Forfeit, ct),
            recent);
    }

    public async Task<(List<AuditLog> Items, int Total)> AuditAsync(AuditFilter f, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.EntityType)) q = q.Where(a => a.EntityType == f.EntityType);
        if (!string.IsNullOrWhiteSpace(f.User)) q = q.Where(a => a.UserName != null && a.UserName.ToLower().Contains(f.User.ToLower()));
        if (f.Day is { } d)
        {
            var from = KokoraTime.StartOfDayUtc(d);
            var to = from.AddDays(1);
            q = q.Where(a => a.At >= from && a.At < to);
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.At).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<List<string>> AuditEntityTypesAsync(CancellationToken ct = default) =>
        db.AuditLogs.Select(a => a.EntityType).Distinct().OrderBy(x => x).ToListAsync(ct);
}
