using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Kokora.Web.TagHelpers;

/// <summary>
/// Champ de formulaire complet (libellé, saisie, aide, erreur) aux couleurs du design system.
/// &lt;k-field asp-for="Name" /&gt; ; type="textarea|number|color|date|datetime|time|checkbox|select|multiselect|file".
/// Les attributs de validation (required, min/max, maxlength, pattern) sont déduits des DataAnnotations.
/// </summary>
[HtmlTargetElement("k-field", Attributes = "asp-for", TagStructure = TagStructure.WithoutEndTag)]
public class FieldTagHelper : TagHelper
{
    [HtmlAttributeName("asp-for")] public ModelExpression For { get; set; } = null!;
    public string? Type { get; set; }
    public string? Label { get; set; }
    public string? Hint { get; set; }
    public string? Placeholder { get; set; }
    public IEnumerable<SelectListItem>? Items { get; set; }
    /// <summary>Texte de l'option vide d'une liste (null = pas d'option vide).</summary>
    public string? Empty { get; set; }
    /// <summary>Active la recherche (Tom Select) sur une liste.</summary>
    public bool Search { get; set; }
    /// <summary>Liste multiple où l'on peut saisir une nouvelle valeur (ex. mots-clés).</summary>
    public bool Create { get; set; }
    public string? Accept { get; set; }
    public string? Class { get; set; }
    public bool Autofocus { get; set; }
    public string? Inputmode { get; set; }
    public string? Autocomplete { get; set; }

    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var name = For.Name;
        var id = TagBuilder.CreateSanitizedId(name, "_");
        var meta = For.Metadata;
        var label = Label ?? meta.DisplayName ?? meta.PropertyName ?? name;
        var type = (Type ?? Infer()).ToLowerInvariant();
        var state = ViewContext.ViewData.ModelState.TryGetValue(name, out var entry) ? entry : null;
        var errors = state?.Errors.Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)).ToList() ?? [];
        var attempted = state?.AttemptedValue;
        var attrs = ValidationAttributes(type);
        if (errors.Count > 0) attrs.Append($" aria-invalid=\"true\" aria-describedby=\"{id}-err\"");
        else if (Hint is not null) attrs.Append($" aria-describedby=\"{id}-hint\"");
        if (Autofocus) attrs.Append(" autofocus");
        if (Inputmode is not null) attrs.Append($" inputmode=\"{E(Inputmode)}\"");
        if (Autocomplete is not null) attrs.Append($" autocomplete=\"{E(Autocomplete)}\"");
        if (Placeholder is not null) attrs.Append($" placeholder=\"{E(Placeholder)}\"");

        var html = new StringBuilder();
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "field" + (Class is null ? "" : " " + Class));

        if (type == "checkbox")
        {
            var isChecked = attempted is not null ? attempted.Contains("true", StringComparison.OrdinalIgnoreCase) : For.Model is true;
            html.Append($"<label class=\"check\" for=\"{id}\"><input type=\"checkbox\" id=\"{id}\" name=\"{E(name)}\" value=\"true\"{(isChecked ? " checked" : "")}{attrs}>");
            html.Append($"<span>{E(label)}</span></label>");
            // Valeur envoyée si la case n'est pas cochée (liaison du booléen).
            html.Append($"<input type=\"hidden\" name=\"{E(name)}\" value=\"false\">");
        }
        else
        {
            html.Append($"<label for=\"{id}\">{E(label)}</label>");
            // Un mot de passe n'est jamais réaffiché dans la page.
            var value = type == "password" ? "" : attempted ?? Format(For.Model, type);
            switch (type)
            {
                case "textarea":
                    html.Append($"<textarea id=\"{id}\" name=\"{E(name)}\" class=\"input textarea\" rows=\"4\"{attrs}>{E(value)}</textarea>");
                    break;
                case "select":
                case "multiselect":
                    var multiple = type == "multiselect";
                    var selected = multiple
                        ? (For.Model as System.Collections.IEnumerable)?.Cast<object>().Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToHashSet() ?? []
                        : [value];
                    if (multiple && state?.RawValue is string[] raw) selected = raw.ToHashSet();
                    html.Append($"<select id=\"{id}\" name=\"{E(name)}\" class=\"input\"{(multiple ? " multiple" : "")}{(Search || multiple ? " data-tom" : "")}{(Create ? " data-tom-create" : "")}{attrs}>");
                    if (Empty is not null) html.Append($"<option value=\"\">{E(Empty)}</option>");
                    foreach (var group in (Items ?? []).GroupBy(i => i.Group?.Name))
                    {
                        if (group.Key is not null) html.Append($"<optgroup label=\"{E(group.Key)}\">");
                        foreach (var item in group)
                        {
                            var isSel = item.Selected && attempted is null && For.Model is null || selected.Contains(item.Value);
                            html.Append($"<option value=\"{E(item.Value)}\"{(isSel ? " selected" : "")}{(item.Disabled ? " disabled" : "")}>{E(item.Text)}</option>");
                        }
                        if (group.Key is not null) html.Append("</optgroup>");
                    }
                    html.Append("</select>");
                    break;
                case "color":
                    html.Append($"<div class=\"flex items-center gap-2\"><input type=\"color\" id=\"{id}\" name=\"{E(name)}\" value=\"{E(value)}\" class=\"color-input\"{attrs}>");
                    html.Append($"<code class=\"num text-[13px] text-ink-2\" data-color-value=\"{id}\">{E(value?.ToUpperInvariant())}</code></div>");
                    break;
                case "file":
                    html.Append($"<input type=\"file\" id=\"{id}\" name=\"{E(name)}\" class=\"input file\"{(Accept is null ? "" : $" accept=\"{E(Accept)}\"")}{attrs}>");
                    break;
                case "datetime":
                    html.Append($"<input type=\"text\" id=\"{id}\" name=\"{E(name)}\" value=\"{E(value)}\" class=\"input num\" data-datetime autocomplete=\"off\"{attrs}>");
                    break;
                case "date":
                    html.Append($"<input type=\"text\" id=\"{id}\" name=\"{E(name)}\" value=\"{E(value)}\" class=\"input num\" data-date autocomplete=\"off\"{attrs}>");
                    break;
                default:
                    var htmlType = type is "number" or "time" or "email" or "tel" or "password" ? type : "text";
                    var num = type is "number" or "time" ? " num" : "";
                    html.Append($"<input type=\"{htmlType}\" id=\"{id}\" name=\"{E(name)}\" value=\"{E(value)}\" class=\"input{num}\"{attrs}>");
                    break;
            }
        }

        if (Hint is not null && errors.Count == 0) html.Append($"<span class=\"hint\" id=\"{id}-hint\">{E(Hint)}</span>");
        if (errors.Count > 0) html.Append($"<span class=\"error\" id=\"{id}-err\" role=\"alert\">{E(string.Join(" ", errors))}</span>");
        output.Content.SetHtmlContent(html.ToString());
    }

    private string Infer()
    {
        var t = Nullable.GetUnderlyingType(For.Metadata.ModelType) ?? For.Metadata.ModelType;
        if (t == typeof(bool)) return "checkbox";
        if (t == typeof(int) || t == typeof(decimal) || t == typeof(double)) return Items is null ? "number" : "select";
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return "datetime";
        if (t == typeof(DateOnly)) return "date";
        if (t == typeof(TimeOnly)) return "time";
        if (t.IsEnum || Items is not null) return "select";
        if (typeof(IFormFile).IsAssignableFrom(t)) return "file";
        if (t != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return "multiselect";
        return "text";
    }

    private static string Format(object? model, string type) => model switch
    {
        null => "",
        DateTime d => d.ToString(type == "date" ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
        Enum e => Convert.ToInt32(e).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => model.ToString() ?? ""
    };

    private StringBuilder ValidationAttributes(string type)
    {
        var sb = new StringBuilder();
        var prop = For.Metadata.ContainerType?.GetProperty(For.Metadata.PropertyName ?? "");
        if (prop is null) return sb;
        if (type != "checkbox" && type != "multiselect" && prop.GetCustomAttributes(typeof(RequiredAttribute), true).Length > 0)
            sb.Append(" required");
        foreach (var a in prop.GetCustomAttributes(true))
            switch (a)
            {
                case StringLengthAttribute sl: sb.Append($" maxlength=\"{sl.MaximumLength}\""); break;
                case RangeAttribute r when type == "number": sb.Append($" min=\"{r.Minimum}\" max=\"{r.Maximum}\""); break;
            }
        return sb;
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
}
