using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Controllers;

/// <summary>Rédaction des infos (accessible aux rédacteurs).</summary>
[Route("admin/infos")]
public class InfosController(ArticleAdminService articles, PhotoAdminService photos, Lookups lookups, AdminSeason season, IAppDbContext db)
    : AdminController
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "infos";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? statut, string? q, int page = 1, CancellationToken ct = default)
    {
        var status = statut switch
        {
            "brouillons" => ArticleStatus.Draft, "programmees" => ArticleStatus.Scheduled,
            "publiees" => ArticleStatus.Published, "archivees" => ArticleStatus.Archived, _ => (ArticleStatus?)null
        };
        var (items, total) = await articles.ListAsync(status, q, page, ct);
        var vm = new ArticlesPageVm
        {
            Items = items, Total = total, Page = page, Status = statut, Search = q, Counts = await articles.CountsAsync(ct)
        };
        if (IsHtmx && page > 1) return PartialView("_Rows", vm); // défilement infini
        return IsHtmx ? PartialView("_List", vm) : View(vm);
    }

    [HttpGet("nouvelle")]
    public async Task<IActionResult> Create(CancellationToken ct) => View("Form", await FormAsync(new ArticleInput(), null, ct));

    [HttpGet("{id:int}/modifier")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var a = await articles.GetAsync(id, ct);
        return View("Form", await FormAsync(ArticleAdminService.ToInput(a), a, ct));
    }

    [HttpPost("enregistrer")]
    public async Task<IActionResult> Save(ArticleFormVm vm, CancellationToken ct)
    {
        if (vm.Cover is { Length: > IImageStore.MaxBytes })
            ModelState.AddModelError(nameof(vm.Cover), "Image trop lourde (8 Mo maximum).");
        if (ModelState.IsValid)
        {
            try
            {
                await using var cover = vm.Cover?.OpenReadStream();
                var isNew = vm.Input.Id == 0;
                var id = await articles.SaveAsync(vm.Input, cover, ct);
                Flash(vm.Input.Mode switch
                {
                    PublishMode.Now => "Info publiée.",
                    PublishMode.Scheduled => $"Publication programmée le {vm.Input.PublishAtLocal:dd/MM/yyyy à HH\\hmm}.",
                    PublishMode.Archived => "Info archivée (retirée du site).",
                    _ => isNew ? "Brouillon créé. Vous pouvez ajouter des photos." : "Brouillon enregistré."
                });
                return Redirect($"/admin/infos/{id}/modifier");
            }
            catch (BusinessRuleException ex) { AddError(ex, "Input"); }
            catch (InvalidImageException ex) { ModelState.AddModelError(nameof(vm.Cover), ex.Message); }
        }
        var existing = vm.Input.Id == 0 ? null : await articles.GetAsync(vm.Input.Id, ct);
        return View("Form", await FormAsync(vm.Input, existing, ct));
    }

    [HttpPost("{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await articles.DeleteAsync(id, ct);
        Flash("Info supprimée.");
        return Redirect("/admin/infos");
    }

    /// <summary>Image insérée dans le texte depuis l'éditeur : renvoie son adresse.</summary>
    [HttpPost("images")]
    public async Task<IActionResult> UploadImage(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "Aucun fichier reçu." });
        if (file.Length > IImageStore.MaxBytes) return BadRequest(new { error = "Image trop lourde (8 Mo maximum)." });
        try
        {
            await using var s = file.OpenReadStream();
            return Json(new { url = await articles.UploadInlineImageAsync(s, ct) });
        }
        catch (InvalidImageException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // ------------------------------------------------------------ Catégories

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct) =>
        View(new CategoriesPageVm { Items = await articles.CategoriesAsync(ct) });

    [HttpPost("categories")]
    public async Task<IActionResult> SaveCategory([Bind(Prefix = "NewCategory")] CategoryInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View("Categories", new CategoriesPageVm { Items = await articles.CategoriesAsync(ct), NewCategory = input });
        try
        {
            await articles.SaveCategoryAsync(input, ct);
            Flash(input.Id == 0 ? "Catégorie ajoutée." : "Catégorie renommée.");
            return Redirect("/admin/infos/categories");
        }
        catch (BusinessRuleException ex)
        {
            AddError(ex, "NewCategory");
            return View("Categories", new CategoriesPageVm { Items = await articles.CategoriesAsync(ct), NewCategory = input });
        }
    }

    [HttpPost("categories/{id:int}/renommer")]
    public async Task<IActionResult> RenameCategory(int id, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) FlashError("Nom invalide (1 à 80 caractères).");
        else
        {
            await articles.SaveCategoryAsync(new CategoryInput { Id = id, Name = name }, ct);
            Flash("Catégorie renommée.");
        }
        return Redirect("/admin/infos/categories");
    }

    [HttpPost("categories/{id:int}/supprimer")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        await articles.DeleteCategoryAsync(id, ct);
        Flash("Catégorie supprimée.");
        return Redirect("/admin/infos/categories");
    }

    [HttpPost("categories/ordre")]
    public async Task<IActionResult> ReorderCategories(List<int> ids, CancellationToken ct)
    {
        await articles.ReorderCategoriesAsync(ids, ct);
        return NoContent();
    }

    // ------------------------------------------------------------

    private async Task<ArticleFormVm> FormAsync(ArticleInput input, Kokora.Domain.Content.Article? existing, CancellationToken ct)
    {
        var categories = await articles.CategoriesAsync(ct);
        var tags = await articles.TagsAsync(ct);
        var seasonId = (await season.GetAsync(ct))?.Id ?? -1;
        // Matchs proposés : saison de travail + ceux déjà liés (même d'une autre saison).
        var linked = input.MatchIds;
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.Phase.Competition.SeasonId == seasonId || linked.Contains(m.Id))
            .OrderByDescending(m => m.KickoffAt)
            .Select(m => new
            {
                m.Id, m.KickoffAt, Home = m.HomeClub != null ? m.HomeClub.ShortName : m.HomePlaceholder ?? "?",
                Away = m.AwayClub != null ? m.AwayClub.ShortName : m.AwayPlaceholder ?? "?", Comp = m.Phase.Competition.ShortName
            }).ToListAsync(ct);
        return new ArticleFormVm
        {
            Input = input,
            Existing = existing,
            Categories = categories.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList(),
            Tags = tags.Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToList(),
            Clubs = await lookups.ClubsAsync(false, ct),
            Matches = matches.Select(m => new SelectListItem(
                $"{m.Home} - {m.Away}{(m.KickoffAt is { } k ? " · " + KokoraTime.ToLocal(k).ToString("dd/MM") : "")} · {m.Comp}", m.Id.ToString())).ToList(),
            Photos = existing is null ? [] : await photos.ListAsync(PhotoOwner.Article(existing.Id), ct),
        };
    }
}
