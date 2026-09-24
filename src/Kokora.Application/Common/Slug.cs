using System.Globalization;
using System.Text;

namespace Kokora.Application.Common;

public static class Slug
{
    /// <summary>« ASC Jeanne d'Arc » → « asc-jeanne-d-arc ».</summary>
    public static string From(string text, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var dash = false;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
    }

    /// <summary>Ajoute -2, -3… jusqu'à obtenir un slug libre.</summary>
    public static async Task<string> UniqueAsync(string text, Func<string, Task<bool>> exists)
    {
        var baseSlug = From(text);
        if (baseSlug.Length == 0) baseSlug = "element";
        var slug = baseSlug;
        for (var i = 2; await exists(slug); i++) slug = $"{baseSlug}-{i}";
        return slug;
    }
}
