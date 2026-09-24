namespace Kokora.Application.Abstractions;

/// <summary>Nettoie le HTML produit par l'éditeur d'articles (liste blanche de balises et d'attributs).</summary>
public interface IHtmlCleaner
{
    string Clean(string? html);

    /// <summary>Texte brut (résumés automatiques, temps de lecture, recherche).</summary>
    string ToPlainText(string? html);
}
