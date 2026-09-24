using System.Text.Json;
using System.Text.Json.Serialization;
using Kokora.Application.Public;
using Kokora.Domain.Enums;

namespace Kokora.Web.Models;

/// <summary>Données structurées schema.org (JSON-LD) pour les moteurs de recherche : match, info, équipe.</summary>
public static class StructuredData
{
    // Encodeur par défaut : « < » et « > » échappés, aucun risque de fermer la balise script.
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string Json(Dictionary<string, object?> o) => JsonSerializer.Serialize(WithoutNulls(o), Options);

    /// <summary>Retire les propriétés vides (logo absent, date inconnue…), y compris dans les objets imbriqués.</summary>
    private static Dictionary<string, object?> WithoutNulls(Dictionary<string, object?> d) =>
        d.Where(kv => kv.Value is not null)
         .ToDictionary(kv => kv.Key, kv => kv.Value is Dictionary<string, object?> inner ? WithoutNulls(inner) : kv.Value);

    private static Dictionary<string, object?> Team(TeamVm? t, string? placeholder, string baseUrl) => new Dictionary<string, object?>
    {
        ["@type"] = "SportsTeam",
        ["name"] = t?.Name ?? placeholder,
        ["url"] = t is null ? null : baseUrl + t.Url,
        ["logo"] = t?.LogoUrl is { } logo ? baseUrl + logo : null
    };

    public static string Match(MatchDetailVm d, string url, string baseUrl)
    {
        var m = d.Match;
        return Json(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "SportsEvent",
            ["name"] = $"{m.Home?.Name ?? m.HomePlaceholder} - {m.Away?.Name ?? m.AwayPlaceholder}",
            ["description"] = $"{d.Competition.Name} · {m.Stage}",
            ["sport"] = "Football",
            ["url"] = url,
            ["startDate"] = m.KickoffAt?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            ["eventStatus"] = m.Status switch
            {
                MatchStatus.Postponed => "https://schema.org/EventPostponed",
                MatchStatus.Abandoned => "https://schema.org/EventCancelled",
                _ => "https://schema.org/EventScheduled"
            },
            ["eventAttendanceMode"] = "https://schema.org/OfflineEventAttendanceMode",
            ["location"] = new Dictionary<string, object?>
            {
                ["@type"] = "Place",
                ["name"] = m.Stadium ?? "Nguékokh",
                ["address"] = new Dictionary<string, object?>
                {
                    ["@type"] = "PostalAddress", ["addressLocality"] = "Nguékokh", ["addressRegion"] = "Thiès", ["addressCountry"] = "SN"
                }
            },
            ["homeTeam"] = Team(m.Home, m.HomePlaceholder, baseUrl),
            ["awayTeam"] = Team(m.Away, m.AwayPlaceholder, baseUrl),
            ["organizer"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = "ODCAV de Mbour" }
        });
    }

    public static string Article(ArticleDetailVm a, string url, string baseUrl) => Json(new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "NewsArticle",
        ["headline"] = a.Card.Title.Length > 110 ? a.Card.Title[..110] : a.Card.Title,
        ["description"] = a.Card.Summary,
        ["url"] = url,
        ["mainEntityOfPage"] = url,
        ["datePublished"] = a.Card.PublishedAt.ToString("yyyy-MM-ddTHH:mm:sszzz"),
        ["dateModified"] = (a.UpdatedAt ?? a.Card.PublishedAt).ToString("yyyy-MM-ddTHH:mm:sszzz"),
        ["image"] = baseUrl + (a.Card.CoverUrl ?? "/icons/og-default.png"),
        ["author"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = a.AuthorName ?? "Kokora" },
        ["publisher"] = new Dictionary<string, object?>
        {
            ["@type"] = "Organization", ["name"] = "Kokora",
            ["logo"] = new Dictionary<string, object?> { ["@type"] = "ImageObject", ["url"] = baseUrl + "/icons/icon-512.png" }
        },
        ["inLanguage"] = "fr"
    });

    public static string TeamPage(TeamVm t, string? neighborhood, string baseUrl) => Json(new Dictionary<string, object?>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "SportsTeam",
        ["name"] = t.Name,
        ["alternateName"] = t.ShortName,
        ["sport"] = "Football",
        ["url"] = baseUrl + t.Url,
        ["logo"] = t.LogoUrl is { } logo ? baseUrl + logo : null,
        ["location"] = new Dictionary<string, object?> { ["@type"] = "Place", ["name"] = neighborhood is null ? "Nguékokh" : $"{neighborhood}, Nguékokh" }
    });
}
