using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Kokora.Web.TagHelpers;

/// <summary>
/// Ajoute le jeton anti-CSRF à tout &lt;form method="post"&gt; écrit avec un simple attribut action="…".
/// (Le tag helper standard ne le fait que pour les formulaires en asp-action / asp-controller.)
/// </summary>
[HtmlTargetElement("form", Attributes = "method")]
public class AntiforgeryFormTagHelper(IHtmlGenerator generator) : TagHelper
{
    // Après le FormTagHelper standard.
    public override int Order => 1000;

    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var method = output.Attributes["method"]?.Value?.ToString();
        if (!string.Equals(method, "post", StringComparison.OrdinalIgnoreCase)) return;
        // Déjà pris en charge par le tag helper standard, ou désactivé explicitement.
        if (context.AllAttributes.Any(a => a.Name.StartsWith("asp-", StringComparison.OrdinalIgnoreCase))) return;
        var action = output.Attributes["action"]?.Value?.ToString();
        if (action is not null && Uri.TryCreate(action, UriKind.Absolute, out _)) return; // jamais vers un autre site
        output.PostContent.AppendHtml(generator.GenerateAntiforgery(ViewContext));
    }
}
