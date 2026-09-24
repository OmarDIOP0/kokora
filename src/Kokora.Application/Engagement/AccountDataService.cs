using Kokora.Application.Abstractions;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Engagement;

/// <summary>Données rattachées à un compte : équipes suivies, et suppression complète à la demande de l'utilisateur.</summary>
public class AccountDataService(IAppDbContext db)
{
    public const int MaxFavorites = 50;

    public Task<List<int>> FavoritesAsync(string userId, CancellationToken ct = default) =>
        db.FavoriteClubs.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.CreatedAt).Select(f => f.ClubId).ToListAsync(ct);

    /// <summary>
    /// Enregistre les équipes suivies. <paramref name="merge"/> : à la connexion, les équipes suivies sur l'appareil
    /// s'ajoutent à celles du compte (rien n'est perdu) ; sinon la liste remplace celle du compte.
    /// </summary>
    public async Task<List<int>> SaveFavoritesAsync(string userId, IEnumerable<int> clubIds, bool merge, CancellationToken ct = default)
    {
        var wanted = clubIds.Distinct().ToList();
        var valid = await db.Clubs.Where(c => wanted.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        var current = await db.FavoriteClubs.Where(f => f.UserId == userId).ToListAsync(ct);
        var target = (merge ? current.Select(f => f.ClubId).Concat(valid) : valid).Distinct().Take(MaxFavorites).ToHashSet();
        db.FavoriteClubs.RemoveRange(current.Where(f => !target.Contains(f.ClubId)));
        foreach (var id in target.Where(id => current.All(f => f.ClubId != id)))
            db.FavoriteClubs.Add(new FavoriteClub { UserId = userId, ClubId = id });
        await db.SaveChangesAsync(ct);
        return await FavoritesAsync(userId, ct);
    }

    /// <summary>Supprime tout ce qui est lié au compte (le compte lui-même est supprimé par Identity).</summary>
    public async Task DeleteAllAsync(string userId, CancellationToken ct = default)
    {
        await db.Predictions.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ManOfTheMatchVotes.Where(v => v.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Comments.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.FavoriteClubs.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PushSubscriptions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
    }
}
