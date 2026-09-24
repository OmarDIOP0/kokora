using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Kokora.Application.Abstractions;

namespace Kokora.Infrastructure.Content;

/// <summary>
/// Liste blanche stricte : paragraphes, intertitres, listes, citations, liens et images.
/// Aucun style, script, iframe ni attribut d'évènement ne survit. Les liens externes s'ouvrent dans un nouvel onglet,
/// sans transmettre la page d'origine. Les images doivent venir du site (/uploads/…) ou d'une adresse https.
/// </summary>
public partial class HtmlCleaner : IHtmlCleaner
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlCleaner()
    {
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "p", "br", "strong", "b", "em", "i", "u", "s", "h2", "h3", "blockquote", "ul", "ol", "li", "a", "img"
            },
            AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "src", "alt", "title" },
            AllowedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "tel" },
            UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "src" },
            AllowedCssProperties = new HashSet<string>(),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
        });
        _sanitizer.KeepChildNodes = true; // une balise refusée (ex. <span>, <div>) laisse son texte
        // … sauf les balises dont le contenu n'est pas du texte lisible : supprimées avec leur contenu.
        _sanitizer.RemovingTag += (_, e) =>
        {
            if (e.Tag.LocalName is "script" or "style" or "iframe" or "object" or "embed" or "noscript" or "template" or "svg" or "math"
                or "textarea" or "select" or "button")
                e.Tag.InnerHtml = "";
        };
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not AngleSharp.Dom.IElement el) return;
            switch (el.LocalName)
            {
                case "a":
                    var href = el.GetAttribute("href") ?? "";
                    if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        el.SetAttribute("target", "_blank");
                        el.SetAttribute("rel", "noopener nofollow");
                    }
                    break;
                case "img":
                    var src = el.GetAttribute("src") ?? "";
                    if (!src.StartsWith("/uploads/", StringComparison.Ordinal) && !src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        el.Remove();
                    else el.SetAttribute("loading", "lazy");
                    break;
            }
        };
        // Attributs ajoutés par le post-traitement.
        _sanitizer.AllowedAttributes.Add("target");
        _sanitizer.AllowedAttributes.Add("rel");
        _sanitizer.AllowedAttributes.Add("loading");
    }

    public string Clean(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        // Quill insère des espaces insécables entre tous les mots : on rend des espaces normales (retour à la ligne correct sur mobile).
        var cleaned = _sanitizer.Sanitize(html.Replace("&nbsp;", " ").Replace(' ', ' '));
        cleaned = EmptyParagraphs().Replace(cleaned, "").Trim();
        return cleaned;
    }

    public string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = Tags().Replace(BlockEnds().Replace(html, " "), "");
        return Spaces().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex(@"<p>(\s|<br\s*/?>)*</p>", RegexOptions.IgnoreCase)]
    private static partial Regex EmptyParagraphs();

    [GeneratedRegex(@"</(p|h2|h3|li|blockquote)>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnds();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
