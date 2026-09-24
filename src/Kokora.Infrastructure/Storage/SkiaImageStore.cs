using Kokora.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SkiaSharp;

namespace Kokora.Infrastructure.Storage;

/// <summary>
/// Stocke les images dans wwwroot/uploads (ou Storage:UploadsPath).
/// Tout fichier est décodé : un fichier qui n'est pas réellement une image est refusé, quel que soit son extension.
/// </summary>
public class SkiaImageStore : IImageStore
{
    private const int WebpQuality = 80;
    private readonly string _root;
    private readonly string _publicPrefix = "/uploads";

    public SkiaImageStore(IHostEnvironment env, IConfiguration config)
    {
        _root = config["Storage:UploadsPath"] is { Length: > 0 } custom
            ? custom
            : Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(_root);
    }

    public async Task<IReadOnlyList<SavedImage>> SaveImagesAsync(Stream input, string folder, string baseName,
        IReadOnlyList<ImageVariant> variants, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(input, buffer, IImageStore.MaxBytes, ct);
        buffer.Position = 0;

        using var codec = SKCodec.Create(buffer)
            ?? throw new InvalidImageException("Le fichier n'est pas une image valide (JPEG, PNG ou WebP).");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png
            or SKEncodedImageFormat.Webp or SKEncodedImageFormat.Gif or SKEncodedImageFormat.Heif))
            throw new InvalidImageException("Format d'image non pris en charge.");
        if (codec.Info.Width * (long)codec.Info.Height > 40_000_000)
            throw new InvalidImageException("Image trop grande (40 mégapixels maximum).");

        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidImageException("Impossible de lire cette image.");
        using var source = ApplyOrientation(decoded, codec.EncodedOrigin);

        var safeFolder = string.Concat(folder.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
        var dir = Path.Combine(_root, safeFolder);
        Directory.CreateDirectory(dir);
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var safeName = string.Concat(baseName.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
        if (safeName.Length == 0) safeName = "image";

        var urls = new List<SavedImage>(variants.Count);
        foreach (var v in variants)
        {
            using var resized = Render(source, v);
            using var data = resized.Encode(SKEncodedImageFormat.Webp, WebpQuality);
            var file = $"{safeName}-{stamp}{v.Suffix}.webp";
            await using (var fs = File.Create(Path.Combine(dir, file)))
                data.SaveTo(fs);
            urls.Add(new SavedImage($"{_publicPrefix}/{safeFolder}/{file}", resized.Width, resized.Height));
        }
        return urls;
    }

    public Task DeleteAsync(string? publicUrl)
    {
        if (string.IsNullOrEmpty(publicUrl) || !publicUrl.StartsWith(_publicPrefix + "/", StringComparison.Ordinal))
            return Task.CompletedTask;
        var relative = publicUrl[(_publicPrefix.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (full.StartsWith(Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase) && File.Exists(full))
            File.Delete(full);
        return Task.CompletedTask;
    }

    private static SKImage Render(SKBitmap src, ImageVariant v)
    {
        var (sw, sh) = ((float)src.Width, (float)src.Height);
        int outW, outH;
        SKRect dest;
        SKRect srcRect = new(0, 0, sw, sh);

        switch (v.Fit)
        {
            case ImageFit.Cover:
            {
                outW = v.Width; outH = v.Height;
                var scale = Math.Max(outW / sw, outH / sh);
                var cw = outW / scale; var ch = outH / scale;
                srcRect = SKRect.Create((sw - cw) / 2, (sh - ch) / 2, cw, ch);
                dest = new SKRect(0, 0, outW, outH);
                break;
            }
            case ImageFit.Contain:
            {
                outW = v.Width; outH = v.Height;
                var scale = Math.Min(1f, Math.Min(outW / sw, outH / sh));
                // Petit logo : on l'agrandit jusqu'au cadre pour éviter un rendu minuscule.
                if (sw < outW && sh < outH) scale = Math.Min(outW / sw, outH / sh);
                var w = sw * scale; var h = sh * scale;
                dest = SKRect.Create((outW - w) / 2, (outH - h) / 2, w, h);
                break;
            }
            default:
            {
                var scale = Math.Min(1f, Math.Min(v.Width / sw, v.Height / sh));
                outW = Math.Max(1, (int)Math.Round(sw * scale));
                outH = Math.Max(1, (int)Math.Round(sh * scale));
                dest = new SKRect(0, 0, outW, outH);
                break;
            }
        }

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, srcRect, dest, new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>Redresse les photos prises au téléphone (orientation EXIF).</summary>
    private static SKBitmap ApplyOrientation(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return bmp.Copy();
        var rotate90 = origin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom
            or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var result = new SKBitmap(rotate90 ? bmp.Height : bmp.Width, rotate90 ? bmp.Width : bmp.Height);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, bmp.Width / 2f, bmp.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, bmp.Width / 2f, 0); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, bmp.Height / 2f); break;
        }
        canvas.DrawBitmap(bmp, 0, 0);
        return result;
    }

    private static async Task CopyLimitedAsync(Stream input, Stream output, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max) throw new InvalidImageException($"Image trop lourde ({max / 1024 / 1024} Mo maximum).");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
