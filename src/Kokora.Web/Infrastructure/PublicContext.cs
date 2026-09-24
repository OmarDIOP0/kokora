using Kokora.Application.Public;

namespace Kokora.Web.Infrastructure;

/// <summary>
/// Contexte d'une page publique : saison courante, compétitions, compétition choisie (mémorisée dans un cookie)
/// et équipes suivies (cookie écrit par le navigateur, voir favorites.js).
/// </summary>
public class PublicContext(MatchQueryService queries, IHttpContextAccessor http)
{
    public const string CompetitionCookie = "k-comp";
    public const string FavoritesCookie = "k-favs";

    private bool _loaded;
    public SeasonVm? Season { get; private set; }
    public IReadOnlyList<CompetitionVm> Competitions { get; private set; } = [];
    public CompetitionVm? Selected { get; private set; }
    public IReadOnlySet<int> Favorites { get; private set; } = new HashSet<int>();

    /// <summary>
    /// Charge le contexte. <paramref name="competitionSlug"/> : « toutes », un slug de compétition, ou null (dernier choix mémorisé).
    /// </summary>
    public async Task LoadAsync(string? competitionSlug, CancellationToken ct = default)
    {
        if (_loaded) return;
        _loaded = true;
        var ctx = http.HttpContext!;
        Favorites = (ctx.Request.Cookies[FavoritesCookie] ?? "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0).Where(id => id > 0).Take(50).ToHashSet();

        Season = await queries.CurrentSeasonAsync(ct);
        if (Season is null) return;
        Competitions = await queries.CompetitionsAsync(Season.Id, ct);

        var slug = competitionSlug ?? ctx.Request.Cookies[CompetitionCookie];
        Selected = Competitions.FirstOrDefault(c => c.Slug == slug);
        if (competitionSlug is not null)
            ctx.Response.Cookies.Append(CompetitionCookie, Selected?.Slug ?? "toutes", new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(180), SameSite = SameSiteMode.Lax, IsEssential = true, HttpOnly = false
            });
    }
}
