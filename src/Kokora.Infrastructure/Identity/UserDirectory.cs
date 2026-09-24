using Kokora.Application.Abstractions;
using Kokora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Infrastructure.Identity;

public class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<Dictionary<string, string>> DisplayNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.DisplayName) ? "Supporter" : u.DisplayName, ct);
    }

    public async Task<string?> DisplayNameAsync(string userId, CancellationToken ct = default) =>
        (await DisplayNamesAsync([userId], ct)).GetValueOrDefault(userId);
}
