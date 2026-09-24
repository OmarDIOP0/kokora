using System.Text;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin/joueurs")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class PlayersController(PlayerAdminService players, AdminSeason season, Lookups lookups, IPlayerFileReader reader) : AdminController
{
    private const int PageSize = 40;

    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "joueurs";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, int? equipe, int page = 1, CancellationToken ct = default)
    {
        var s = await season.GetAsync(ct);
        if (s is null) return Redirect("/admin/saisons/nouvelle");
        page = Math.Max(1, page);
        var (items, total) = await players.ListAsync(s.Value.Id, q, equipe, page, PageSize, ct);
        var vm = new PlayersPageVm
        {
            Items = items, Total = total, Page = page, PageSize = PageSize, Search = q, ClubId = equipe,
            Clubs = IsHtmx ? [] : await lookups.ClubsAsync(ct: ct)
        };
        ViewData["SeasonName"] = s.Value.Name;
        // Défilement infini : les pages suivantes ne renvoient que les lignes.
        if (IsHtmx) return PartialView(page > 1 ? "_Rows" : "_List", vm);
        return View(vm);
    }

    [HttpGet("nouveau")]
    public async Task<IActionResult> Create(int? equipe, CancellationToken ct) =>
        View("Form", await Form(new PlayerInput { ClubId = equipe }, null, ct));

    [HttpGet("{id:int}/modifier")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var s = await season.GetAsync(ct);
        var (p, m) = await players.GetAsync(id, s!.Value.Id, ct);
        return View("Form", await Form(PlayerAdminService.ToInput(p, m), p.PhotoPath, ct));
    }

    [HttpPost("enregistrer")]
    public async Task<IActionResult> Save(PlayerFormVm vm, string? next, CancellationToken ct)
    {
        var s = await season.GetAsync(ct);
        if (s is null) return Redirect("/admin/saisons/nouvelle");
        if (ModelState.IsValid)
        {
            try
            {
                await using var photo = vm.Photo?.OpenReadStream();
                await players.SaveAsync(vm.Input, s.Value.Id, photo, ct);
                Flash(vm.Input.Id == 0 ? $"{vm.Input.FirstName} {vm.Input.LastName} ajouté." : "Joueur enregistré.");
                // « Enregistrer et ajouter un autre » : on garde l'équipe pour enchaîner la saisie d'un effectif.
                return next == "autre"
                    ? Redirect($"/admin/joueurs/nouveau?equipe={vm.Input.ClubId}")
                    : Redirect(vm.Input.ClubId is { } c ? $"/admin/joueurs?equipe={c}" : "/admin/joueurs");
            }
            catch (BusinessRuleException ex) { AddError(ex, "Input"); }
            catch (InvalidImageException ex) { ModelState.AddModelError(nameof(vm.Photo), ex.Message); }
        }
        string? photoPath = null;
        if (vm.Input.Id != 0) photoPath = (await players.GetAsync(vm.Input.Id, s.Value.Id, ct)).Player.PhotoPath;
        return View("Form", await Form(vm.Input, photoPath, ct));
    }

    [HttpPost("{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await players.DeleteAsync(id, ct);
        Flash("Joueur supprimé.");
        return Redirect("/admin/joueurs");
    }

    [HttpGet("import")]
    public async Task<IActionResult> Import(CancellationToken ct) =>
        View(new ImportPageVm { SeasonName = (await season.GetAsync(ct))?.Name ?? "" });

    [HttpPost("import")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        var s = await season.GetAsync(ct);
        if (s is null) return Redirect("/admin/saisons/nouvelle");
        if (file is null || file.Length == 0)
        {
            FlashError("Choisissez un fichier .xlsx ou .csv.");
            return Redirect("/admin/joueurs/import");
        }
        try
        {
            await using var stream = file.OpenReadStream();
            var rows = reader.Read(stream, file.FileName);
            var report = await players.ImportAsync(rows, s.Value.Id, ct);
            return View(new ImportPageVm { Report = report, SeasonName = s.Value.Name });
        }
        catch (BusinessRuleException ex)
        {
            FlashError(ex.Message);
            return Redirect("/admin/joueurs/import");
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException)
        {
            FlashError("Fichier illisible. Vérifiez qu'il s'agit bien d'un .xlsx ou d'un .csv.");
            return Redirect("/admin/joueurs/import");
        }
    }

    /// <summary>Modèle CSV à remplir (séparateur « ; » pour Excel en français, UTF-8 avec BOM).</summary>
    [HttpGet("modele.csv")]
    public IActionResult Template()
    {
        var csv = string.Join(';', IPlayerFileReader.Columns) + "\r\n" +
                  "Moussa;Exemple;Mouss;Attaquant;ASC Démo 1;9;15/03/2004;\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "modele-joueurs.csv");
    }

    private async Task<PlayerFormVm> Form(PlayerInput input, string? photo, CancellationToken ct) => new()
    {
        Input = input, PhotoPath = photo, Clubs = await lookups.ClubsAsync(ct: ct),
        SeasonName = (await season.GetAsync(ct))?.Name ?? ""
    };
}
