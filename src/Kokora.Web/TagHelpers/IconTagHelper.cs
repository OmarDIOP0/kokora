using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Kokora.Web.TagHelpers;

/// <summary>
/// Insère une icône Lucide en SVG inline, côté serveur (aucun JavaScript, pas de décalage à l'affichage).
/// Usage : &lt;icon name="calendar-days" class="text-brand" /&gt; ; label="…" pour une icône porteuse de sens.
/// </summary>
[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public partial class IconTagHelper(IWebHostEnvironment env) : TagHelper
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    public string Name { get; set; } = "";
    public string? Label { get; set; }
    public string? Class { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        var inner = Cache.GetOrAdd(Name, Load);
        if (inner is null)
        {
            output.SuppressOutput();
            return;
        }

        var a11y = Label is null
            ? "aria-hidden=\"true\""
            : $"role=\"img\" aria-label=\"{System.Net.WebUtility.HtmlEncode(Label)}\"";
        var cls = string.IsNullOrWhiteSpace(Class) ? "icon" : $"icon {Class}";
        output.Content.SetHtmlContent(
            $"<svg class=\"{cls}\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-linecap=\"round\" stroke-linejoin=\"round\" focusable=\"false\" {a11y}>{inner}</svg>");
    }

    private string? Load(string name)
    {
        if (!SafeName().IsMatch(name)) return null;
        var path = Path.Combine(env.ContentRootPath, "Icons", name + ".svg");
        if (!File.Exists(path)) return null;
        var svg = File.ReadAllText(path);
        var start = svg.IndexOf('>', svg.IndexOf("<svg", StringComparison.Ordinal)) + 1;
        var end = svg.LastIndexOf("</svg>", StringComparison.Ordinal);
        return Whitespace().Replace(svg[start..end], " ").Trim();
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex SafeName();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
