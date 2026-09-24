using System.Text.RegularExpressions;
using Kokora.Application.Abstractions;

namespace Kokora.Web.Infrastructure;

/// <summary>
/// Compte les pages publiques affichées (rubrique par rubrique). Sont ignorés : robots et aperçus de liens,
/// préchargements, fragments htmx, administration et compte.
/// </summary>
public static partial class VisitCounting
{
    [GeneratedRegex("bot|crawl|spider|slurp|preview|facebookexternalhit|whatsapp|telegram|monitor|curl|wget|python|headless", RegexOptions.IgnoreCase)]
    private static partial Regex Robots();

    public static string? Section(PathString path)
    {
        var p = path.Value ?? "/";
        if (p == "/") return "matchs";
        var first = p.Trim('/').Split('/')[0];
        return first switch
        {
            "matchs" => "match",
            "classements" or "stats" or "equipes" or "joueurs" or "infos" or "pronostics" or "recherche" or "plus" => first,
            _ => null
        };
    }

    public static IApplicationBuilder UseVisitCounting(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        await next();
        var req = context.Request;
        if (!HttpMethods.IsGet(req.Method) || context.Response.StatusCode != 200) return;
        if (req.Headers.ContainsKey("HX-Request")) return;
        if (req.Headers["Sec-Purpose"].ToString().Contains("prefetch") || req.Headers["Purpose"] == "prefetch") return;
        if (!(context.Response.ContentType ?? "").StartsWith("text/html")) return;
        if (Robots().IsMatch(req.Headers.UserAgent.ToString())) return;
        if (Section(req.Path) is not { } section) return;
        var counter = context.RequestServices.GetRequiredService<IVisitCounter>();
        counter.Count(section);
        // Ouverture depuis l'icône de l'application installée.
        if (req.Query["source"] == "appli") counter.Count("appli");
    });
}
