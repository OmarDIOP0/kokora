namespace Kokora.Application.Abstractions;

public enum ImageFit
{
    /// <summary>L'image entière tient dans le cadre, fond transparent (logos).</summary>
    Contain,
    /// <summary>L'image remplit le cadre, recadrée au centre (photos de joueurs).</summary>
    Cover,
    /// <summary>Réduite pour ne pas dépasser le cadre, proportions conservées (photos d'articles).</summary>
    Inside
}

public record ImageVariant(string Suffix, int Width, int Height, ImageFit Fit);

/// <summary>Image enregistrée : URL publique et dimensions réelles (pour réserver la place à l'affichage).</summary>
public record SavedImage(string Url, int Width, int Height);

public class InvalidImageException(string message) : Exception(message);

/// <summary>Enregistre des images téléversées en WebP redimensionné (validation par décodage réel).</summary>
public interface IImageStore
{
    public const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>Renvoie l'URL publique de chaque variante, dans l'ordre demandé.</summary>
    async Task<IReadOnlyList<string>> SaveAsync(Stream input, string folder, string baseName,
        IReadOnlyList<ImageVariant> variants, CancellationToken ct = default) =>
        (await SaveImagesAsync(input, folder, baseName, variants, ct)).Select(i => i.Url).ToList();

    /// <summary>Comme <see cref="SaveAsync"/>, avec les dimensions de chaque variante.</summary>
    Task<IReadOnlyList<SavedImage>> SaveImagesAsync(Stream input, string folder, string baseName,
        IReadOnlyList<ImageVariant> variants, CancellationToken ct = default);

    Task DeleteAsync(string? publicUrl);
}

public static class ImagePresets
{
    public static readonly ImageVariant[] Logo = [new("", 256, 256, ImageFit.Contain)];
    public static readonly ImageVariant[] PlayerPhoto = [new("", 400, 400, ImageFit.Cover)];
    public static readonly ImageVariant[] Cover = [new("", 1600, 900, ImageFit.Inside), new("-sm", 640, 360, ImageFit.Cover)];
    /// <summary>Photo de galerie : grande image + vignette carrée.</summary>
    public static readonly ImageVariant[] Gallery = [new("", 1600, 1600, ImageFit.Inside), new("-sm", 400, 400, ImageFit.Cover)];
    /// <summary>Image insérée dans le corps d'un article.</summary>
    public static readonly ImageVariant[] Inline = [new("", 1200, 1200, ImageFit.Inside)];
}
