using Kokora.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin;

/// <summary>
/// Saison sur laquelle travaille l'administrateur (cookie), par défaut la saison en cours.
/// </summary>
public class AdminSeason(IAppDbContext db, IHttpContextAccessor http)
{
    public const string Cookie = "k-admin-season";
    private (int Id, string Name, bool IsCurrent)? _resolved;
    private bool _loaded;

    public async Task<(int Id, string Name, bool IsCurrent)?> GetAsync(CancellationToken ct = default)
    {
        if (_loaded) return _resolved;
        _loaded = true;
        var seasons = db.Seasons.AsNoTracking();
        if (int.TryParse(http.HttpContext?.Request.Cookies[Cookie], out var id)
            && await seasons.Where(s => s.Id == id).Select(s => new { s.Id, s.Name, s.IsCurrent }).FirstOrDefaultAsync(ct) is { } chosen)
            return _resolved = (chosen.Id, chosen.Name, chosen.IsCurrent);

        var current = await seasons.OrderByDescending(s => s.IsCurrent).ThenByDescending(s => s.Year)
            .Select(s => new { s.Id, s.Name, s.IsCurrent }).FirstOrDefaultAsync(ct);
        return _resolved = current is null ? null : (current.Id, current.Name, current.IsCurrent);
    }

    public void Set(int seasonId) =>
        http.HttpContext?.Response.Cookies.Append(Cookie, seasonId.ToString(), new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = TimeSpan.FromDays(365),
            Secure = http.HttpContext.Request.IsHttps
        });
}
