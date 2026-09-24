using System.ComponentModel.DataAnnotations;
using Kokora.Domain.Enums;

namespace Kokora.Application.Admin;

/// <summary>Publication choisie dans le formulaire (le statut réel en découle).</summary>
public enum PublishMode
{
    Draft = 1,
    Now = 2,
    Scheduled = 3,
    Archived = 4
}

public class ArticleInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le titre est obligatoire.")]
    [StringLength(200, ErrorMessage = "200 caractères maximum.")]
    [Display(Name = "Titre")]
    public string Title { get; set; } = "";

    [StringLength(120, ErrorMessage = "120 caractères maximum.")]
    [RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "Lettres minuscules, chiffres et tirets uniquement.")]
    [Display(Name = "Adresse (slug)")]
    public string? Slug { get; set; }

    [StringLength(500, ErrorMessage = "500 caractères maximum.")]
    [Display(Name = "Chapeau")]
    public string? Summary { get; set; }

    /// <summary>HTML de l'éditeur, nettoyé côté serveur avant l'enregistrement.</summary>
    [StringLength(200_000, ErrorMessage = "Texte trop long.")]
    public string? Body { get; set; }

    [Display(Name = "Catégorie")]
    public int? CategoryId { get; set; }

    /// <summary>Mots-clés existants (identifiants) ou nouveaux (texte libre).</summary>
    [Display(Name = "Mots-clés")]
    public List<string> Tags { get; set; } = [];

    [Display(Name = "Équipes concernées")]
    public List<int> ClubIds { get; set; } = [];

    [Display(Name = "Matchs concernés")]
    public List<int> MatchIds { get; set; } = [];

    [Display(Name = "Publication")]
    public PublishMode Mode { get; set; } = PublishMode.Draft;

    /// <summary>Date locale (Dakar) de publication programmée.</summary>
    [Display(Name = "Publier le")]
    public DateTime? PublishAtLocal { get; set; }

    [Display(Name = "À la une")]
    public bool IsFeatured { get; set; }

    [Display(Name = "Info importante (notification aux abonnés)")]
    public bool IsImportant { get; set; }

    [Display(Name = "Autoriser les commentaires")]
    public bool AllowComments { get; set; } = true;

    [Display(Name = "Retirer l'image de couverture")]
    public bool RemoveCover { get; set; }
}

public class CategoryInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(80, ErrorMessage = "80 caractères maximum.")]
    [Display(Name = "Nom")]
    public string Name { get; set; } = "";
}

public class PhotoInput
{
    public int Id { get; set; }

    [StringLength(300, ErrorMessage = "300 caractères maximum.")]
    [Display(Name = "Légende")]
    public string? Caption { get; set; }

    [StringLength(120, ErrorMessage = "120 caractères maximum.")]
    [Display(Name = "Crédit photo")]
    public string? Credit { get; set; }
}

public record ArticleListItem(int Id, string Title, string Slug, ArticleStatus Status, DateTimeOffset? PublishedAt, DateTimeOffset UpdatedAt,
    string? Category, string? CoverUrl, bool IsFeatured, bool IsImportant, string? AuthorName, bool IsDemo, int Photos)
{
    /// <summary>Une info programmée dont l'heure est passée est en ligne.</summary>
    public ArticleStatus Effective(DateTimeOffset now) =>
        Status == ArticleStatus.Scheduled && PublishedAt <= now ? ArticleStatus.Published : Status;
}

public record ArticleCounts(int All, int Drafts, int Scheduled, int Published, int Archived);

public record CategoryItem(int Id, string Name, string Slug, int Articles);

public record PhotoItem(int Id, string Url, string ThumbUrl, int Width, int Height, string? Caption, string? Credit);
