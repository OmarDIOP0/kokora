using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Controllers;

/// <summary>Galeries photos des infos et des matchs (accessible aux rédacteurs).</summary>
[Route("admin")]
public class PhotosController(PhotoAdminService photos, IAppDbContext db, AdminSeason season) : AdminController
{
    /// <summary>Matchs de la saison de travail, les plus récents d'abord, avec leur nombre de photos.</summary>
    [HttpGet("photos")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["AdminNav"] = "photos";
        var seasonId = (await season.GetAsync(ct))?.Id ?? -1;
        var now = DateTimeOffset.UtcNow.AddHours(3);
        var items = await db.Matches.AsNoTracking()
            .Where(m => m.Phase.Competition.SeasonId == seasonId && m.KickoffAt != null && m.KickoffAt <= now)
            .OrderByDescending(m => m.KickoffAt).Take(60)
            .Select(m => new MatchPhotoItem(m.Id,
                (m.HomeClub != null ? m.HomeClub.Name : m.HomePlaceholder ?? "?") + " - " + (m.AwayClub != null ? m.AwayClub.Name : m.AwayPlaceholder ?? "?"),
                m.Phase.Competition.Name, m.KickoffAt, db.Photos.Count(p => p.MatchId == m.Id)))
            .ToListAsync(ct);
        return View(items);
    }

    [HttpGet("matchs/{id:int}/photos")]
    public async Task<IActionResult> Match(int id, CancellationToken ct)
    {
        ViewData["AdminNav"] = "matchs";
        var m = await db.Matches.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                Home = x.HomeClub != null ? x.HomeClub.Name : x.HomePlaceholder ?? "À désigner",
                Away = x.AwayClub != null ? x.AwayClub.Name : x.AwayPlaceholder ?? "À désigner",
                x.KickoffAt, Comp = x.Phase.Competition.Name
            }).FirstOrDefaultAsync(ct);
        if (m is null) return NotFound();
        return View(new MatchPhotosVm
        {
            MatchId = id, Title = $"{m.Home} - {m.Away}", Subtitle = m.Comp, KickoffAt = m.KickoffAt,
            Gallery = new GalleryVm(PhotoOwner.Match(id), await photos.ListAsync(PhotoOwner.Match(id), ct))
        });
    }

    [HttpPost("photos/ajouter")]
    public async Task<IActionResult> Add(int? info, int? match, List<IFormFile> files, string? credit, CancellationToken ct)
    {
        var owner = info is { } a ? PhotoOwner.Article(a) : match is { } m ? PhotoOwner.Match(m) : null;
        if (owner is null) return BadRequest();
        var usable = files.Where(f => f.Length > 0).ToList();
        if (usable.Count == 0) FlashError("Choisissez au moins une photo.");
        else
        {
            var tooBig = usable.Where(f => f.Length > IImageStore.MaxBytes).Select(f => $"{f.FileName} : image trop lourde (8 Mo maximum).").ToList();
            var result = await photos.AddAsync(owner,
                usable.Where(f => f.Length <= IImageStore.MaxBytes).Select(f => (f.FileName, (Func<Stream>)f.OpenReadStream)).ToList(), credit, ct);
            var errors = tooBig.Concat(result.Errors).ToList();
            if (errors.Count == 0) Flash(result.Added == 1 ? "Photo ajoutée." : $"{result.Added} photos ajoutées.");
            else FlashError($"{result.Added} photo(s) ajoutée(s). Refusée(s) : {string.Join(" ", errors)}");
        }
        return Redirect(PageOf(owner));
    }

    [HttpPost("photos/{id:int}")]
    public async Task<IActionResult> Update(int id, PhotoInput input, CancellationToken ct)
    {
        input.Id = id;
        if (!ModelState.IsValid) return UnprocessableEntity("Légende ou crédit trop long.");
        await photos.UpdateAsync(input, ct);
        return IsHtmx ? NoContent() : Back("/admin");
    }

    [HttpPost("photos/{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var owner = await photos.DeleteAsync(id, ct);
        Flash("Photo supprimée.");
        return Redirect(PageOf(owner));
    }

    [HttpPost("photos/ordre")]
    public async Task<IActionResult> Reorder(int? info, int? match, List<int> ids, CancellationToken ct)
    {
        await photos.ReorderAsync(new PhotoOwner(info, match), ids, ct);
        return NoContent();
    }

    private static string PageOf(PhotoOwner owner) =>
        owner.ArticleId is { } a ? $"/admin/infos/{a}/modifier#photos" : $"/admin/matchs/{owner.MatchId}/photos";
}
