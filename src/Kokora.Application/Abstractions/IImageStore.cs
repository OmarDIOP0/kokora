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

public class InvalidImageException(string message) : Exception(message);

/// <summary>Enregistre des images téléversées en WebP redimensionné (validation par décodage réel).</summary>
public interface IImageStore
{
    public const long MaxBytes = 8 * 1024 * 1024;

    /// <summary>Renvoie l'URL publique de chaque variante, dans l'ordre demandé.</summary>
    Task<IReadOnlyList<string>> SaveAsync(Stream input, string folder, string baseName,
        IReadOnlyList<ImageVariant> variants, CancellationToken ct = default);

    Task DeleteAsync(string? publicUrl);
}

public static class ImagePresets
{
    public static readonly ImageVariant[] Logo = [new("", 256, 256, ImageFit.Contain)];
    public static readonly ImageVariant[] PlayerPhoto = [new("", 400, 400, ImageFit.Cover)];
    public static readonly ImageVariant[] Cover = [new("", 1600, 900, ImageFit.Inside), new("-sm", 640, 360, ImageFit.Cover)];
}
