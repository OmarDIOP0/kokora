using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin/saisons")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = Kokora.Application.Abstractions.Roles.AdminOrAbove)]
public class SeasonsController(SeasonAdminService seasons, AdminSeason working) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "saisons";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new SeasonsPageVm(await seasons.ListAsync(ct), (await working.GetAsync(ct))?.Id));

    [HttpGet("nouvelle")]
    public IActionResult Create() => View("Form", new SeasonInput
    {
        Year = KokoraTime.Today.Year, Name = $"Saison {KokoraTime.Today.Year}"
    });

    [HttpGet("{id:int}/modifier")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var s = await seasons.GetAsync(id, ct);
        return View("Form", new SeasonInput { Id = s.Id, Year = s.Year, Name = s.Name, IsCurrent = s.IsCurrent });
    }

    [HttpPost("enregistrer")]
    public async Task<IActionResult> Save(SeasonInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await seasons.SaveAsync(input, ct);
                if (input.Id == 0) working.Set(id);
                Flash(input.Id == 0 ? "Saison créée." : "Saison enregistrée.");
                return Redirect("/admin/saisons");
            }
            catch (BusinessRuleException ex) { AddError(ex); }
        }
        return View("Form", input);
    }

    [HttpPost("{id:int}/en-cours")]
    public async Task<IActionResult> SetCurrent(int id, CancellationToken ct)
    {
        await seasons.SetCurrentAsync(id, ct);
        Flash("Saison en cours mise à jour.");
        return Redirect("/admin/saisons");
    }

    [HttpPost("{id:int}/structure-type")]
    public async Task<IActionResult> Structure(int id, CancellationToken ct)
    {
        await seasons.CreateStandardStructureAsync(id, ct);
        working.Set(id);
        Flash("Structure créée : Zonales 5A et 5B, 4 Grandes de chaque zone, Coupe du Maire.");
        return Redirect("/admin/saisons");
    }

    [HttpPost("{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await seasons.DeleteAsync(id, ct);
        Flash("Saison supprimée.");
        return Redirect("/admin/saisons");
    }

    /// <summary>Change la saison de travail de l'administrateur.</summary>
    [HttpPost("choisir")]
    public IActionResult Choose(int id)
    {
        working.Set(id);
        return Back("/admin");
    }
}
