using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin/equipes")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class ClubsController(ClubAdminService clubs) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "equipes";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? zone, CancellationToken ct)
    {
        ViewData["Search"] = q;
        ViewData["Zone"] = zone;
        var list = await clubs.ListAsync(q, zone, ct);
        return IsHtmx ? PartialView("_List", list) : View(list);
    }

    [HttpGet("nouvelle")]
    public IActionResult Create() => View("Form", new ClubFormVm());

    [HttpGet("{id:int}/modifier")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var c = await clubs.GetAsync(id, ct);
        return View("Form", new ClubFormVm { Input = ClubAdminService.ToInput(c), LogoPath = c.LogoPath });
    }

    [HttpPost("enregistrer")]
    public async Task<IActionResult> Save(ClubFormVm vm, CancellationToken ct)
    {
        if (vm.Logo is { Length: > IImageStore.MaxBytes })
            ModelState.AddModelError(nameof(vm.Logo), "Image trop lourde (8 Mo maximum).");
        if (ModelState.IsValid)
        {
            try
            {
                await using var logo = vm.Logo?.OpenReadStream();
                await clubs.SaveAsync(vm.Input, logo, ct);
                Flash(vm.Input.Id == 0 ? $"« {vm.Input.Name} » ajoutée." : "Équipe enregistrée.");
                return Redirect("/admin/equipes");
            }
            catch (BusinessRuleException ex) { AddError(ex, "Input"); }
            catch (InvalidImageException ex) { ModelState.AddModelError(nameof(vm.Logo), ex.Message); }
        }
        if (vm.Input.Id != 0) vm.LogoPath = (await clubs.GetAsync(vm.Input.Id, ct)).LogoPath;
        return View("Form", vm);
    }

    [HttpPost("{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await clubs.DeleteAsync(id, ct);
        Flash("Équipe supprimée.");
        return Redirect("/admin/equipes");
    }
}
