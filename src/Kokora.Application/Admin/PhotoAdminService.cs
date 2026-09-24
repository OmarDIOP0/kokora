using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

/// <summary>Propriétaire d'une galerie : une info ou un match.</summary>
public record PhotoOwner(int? ArticleId, int? MatchId)
{
    public static PhotoOwner Article(int id) => new(id, null);
    public static PhotoOwner Match(int id) => new(null, id);
}

public record PhotoUploadResult(int Added, IReadOnlyList<string> Errors);

/// <summary>Galeries photos (infos et matchs) : envoi multiple, légendes, ordre, suppression.</summary>
public class PhotoAdminService(IAppDbContext db, IImageStore images)
{
    public const int MaxPerUpload = 20;

    public Task<List<PhotoItem>> ListAsync(PhotoOwner owner, CancellationToken ct = default) =>
        Query(owner).AsNoTracking().OrderBy(p => p.Order).ThenBy(p => p.Id)
            .Select(p => new PhotoItem(p.Id, p.Path, p.ThumbnailPath ?? p.Path, p.Width, p.Height, p.Caption, p.Credit))
            .ToListAsync(ct);

    private IQueryable<Photo> Query(PhotoOwner owner) =>
        owner.ArticleId is { } a ? db.Photos.Where(p => p.ArticleId == a) : db.Photos.Where(p => p.MatchId == owner.MatchId);

    /// <summary>Chaque fichier est traité séparément : une image invalide n'empêche pas les autres d'être ajoutées.</summary>
    public async Task<PhotoUploadResult> AddAsync(PhotoOwner owner, IReadOnlyList<(string FileName, Func<Stream> Open)> files,
        string? credit, CancellationToken ct = default)
    {
        await EnsureOwnerAsync(owner, ct);
        if (files.Count > MaxPerUpload)
            throw new BusinessRuleException($"{MaxPerUpload} photos maximum par envoi.");

        var order = await Query(owner).MaxAsync(p => (int?)p.Order, ct) ?? 0;
        var errors = new List<string>();
        var added = new List<Photo>();
        var folder = owner.ArticleId is not null ? "galerie-infos" : "galerie-matchs";
        foreach (var (name, open) in files)
        {
            try
            {
                await using var stream = open();
                var saved = await images.SaveImagesAsync(stream, folder, owner.ArticleId is { } a ? $"info-{a}" : $"match-{owner.MatchId}",
                    ImagePresets.Gallery, ct);
                added.Add(new Photo
                {
                    ArticleId = owner.ArticleId, MatchId = owner.MatchId, Path = saved[0].Url, ThumbnailPath = saved[1].Url,
                    Width = saved[0].Width, Height = saved[0].Height, Order = ++order,
                    Credit = string.IsNullOrWhiteSpace(credit) ? null : credit.Trim()
                });
            }
            catch (InvalidImageException ex) { errors.Add($"{name} : {ex.Message}"); }
        }
        db.Photos.AddRange(added);
        await db.SaveChangesAsync(ct);
        return new PhotoUploadResult(added.Count, errors);
    }

    public async Task UpdateAsync(PhotoInput input, CancellationToken ct = default)
    {
        var photo = await db.Photos.FindAsync([input.Id], ct) ?? throw new NotFoundException("Photo");
        photo.Caption = string.IsNullOrWhiteSpace(input.Caption) ? null : input.Caption.Trim();
        photo.Credit = string.IsNullOrWhiteSpace(input.Credit) ? null : input.Credit.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Renvoie le propriétaire (pour revenir à la bonne page).</summary>
    public async Task<PhotoOwner> DeleteAsync(int id, CancellationToken ct = default)
    {
        var photo = await db.Photos.FindAsync([id], ct) ?? throw new NotFoundException("Photo");
        db.Photos.Remove(photo);
        await db.SaveChangesAsync(ct);
        await images.DeleteAsync(photo.Path);
        await images.DeleteAsync(photo.ThumbnailPath);
        return new PhotoOwner(photo.ArticleId, photo.MatchId);
    }

    public async Task ReorderAsync(PhotoOwner owner, IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        var photos = await Query(owner).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var order = ids.ToList();
        foreach (var p in photos) p.Order = order.IndexOf(p.Id) + 1;
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureOwnerAsync(PhotoOwner owner, CancellationToken ct)
    {
        var exists = owner.ArticleId is { } a
            ? await db.Articles.AnyAsync(x => x.Id == a, ct)
            : owner.MatchId is { } m && await db.Matches.AnyAsync(x => x.Id == m, ct);
        if (!exists) throw new NotFoundException(owner.ArticleId is not null ? "Info" : "Match");
    }
}
