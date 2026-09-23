using System.Globalization;
using System.Net;
using Kokora.Domain.Clubs;
using Kokora.Web.Models;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Kokora.Web.TagHelpers;

/// <summary>
/// Écusson d'une ASC : le logo s'il existe, sinon les initiales sur la couleur de l'équipe.
/// Usage : &lt;crest team="@match.Home" size="lg" /&gt;
/// </summary>
[HtmlTargetElement("crest", TagStructure = TagStructure.WithoutEndTag)]
public class CrestTagHelper : TagHelper
{
    public TeamVm? Team { get; set; }
    /// <summary>"sm" (défaut, 22px), "lg" (40px), "xl" (64px).</summary>
    public string? Size { get; set; }
    public string? Class { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        var cls = "crest";
        if (Size is "lg" or "xl") cls += " " + Size;
        if (!string.IsNullOrWhiteSpace(Class)) cls += " " + Class;
        output.Attributes.SetAttribute("class", cls);
        output.Attributes.SetAttribute("aria-hidden", "true");

        if (Team is null)
        {
            output.Content.SetContent("?");
            return;
        }

        if (!string.IsNullOrEmpty(Team.LogoUrl))
        {
            output.Content.SetHtmlContent(
                $"<img src=\"{WebUtility.HtmlEncode(Team.LogoUrl)}\" alt=\"\" loading=\"lazy\" decoding=\"async\" width=\"64\" height=\"64\">");
            return;
        }

        var (bg, fg) = Colors(Team.Color, Team.Color2);
        output.Attributes.SetAttribute("style", $"--c1:{bg};--c2:{fg}");
        output.Content.SetContent(ClubInitials.From(Team.ShortName.Length > 0 ? Team.ShortName : Team.Name));
    }

    /// <summary>Texte lisible sur la couleur principale : la couleur secondaire si elle contraste assez, sinon noir ou blanc.</summary>
    public static (string Bg, string Fg) Colors(string primary, string? secondary)
    {
        var bg = Normalize(primary) ?? "#5b5f63";
        var fg = Normalize(secondary);
        if (fg is not null && Contrast(bg, fg) >= 3.0) return (bg, fg);
        return (bg, Contrast(bg, "#ffffff") >= Contrast(bg, "#111111") ? "#ffffff" : "#111111");
    }

    private static string? Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        return hex.Length == 7 && int.TryParse(hex[1..], NumberStyles.HexNumber, null, out _) ? hex : null;
    }

    private static double Luminance(string hex)
    {
        static double Channel(int v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        var rgb = int.Parse(hex[1..], NumberStyles.HexNumber);
        return 0.2126 * Channel((rgb >> 16) & 255) + 0.7152 * Channel((rgb >> 8) & 255) + 0.0722 * Channel(rgb & 255);
    }

    public static double Contrast(string a, string b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
}
