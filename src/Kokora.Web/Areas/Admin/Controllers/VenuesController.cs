using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin/lieux")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class VenuesController(VenueAdminService venues) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "lieux";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await Page(null, null, ct));

    [HttpPost("stades")]
    public async Task<IActionResult> SaveStadium([Bind(Prefix = "NewStadium")] StadiumInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Index", await Page(input, null, ct));
        await venues.SaveStadiumAsync(input, ct);
        Flash(input.Id == 0 ? "Stade ajouté." : "Stade enregistré.");
        return Redirect("/admin/lieux");
    }

    [HttpPost("arbitres")]
    public async Task<IActionResult> SaveReferee([Bind(Prefix = "NewReferee")] RefereeInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Index", await Page(null, input, ct));
        await venues.SaveRefereeAsync(input, ct);
        Flash(input.Id == 0 ? "Arbitre ajouté." : "Arbitre enregistré.");
        return Redirect("/admin/lieux");
    }

    [HttpPost("stades/{id:int}/supprimer")]
    public async Task<IActionResult> DeleteStadium(int id, CancellationToken ct)
    {
        await venues.DeleteStadiumAsync(id, ct);
        Flash("Stade supprimé.");
        return Redirect("/admin/lieux");
    }

    [HttpPost("arbitres/{id:int}/supprimer")]
    public async Task<IActionResult> DeleteReferee(int id, CancellationToken ct)
    {
        await venues.DeleteRefereeAsync(id, ct);
        Flash("Arbitre supprimé.");
        return Redirect("/admin/lieux");
    }

    private async Task<VenuesPageVm> Page(StadiumInput? s, RefereeInput? r, CancellationToken ct) => new()
    {
        Stadiums = await venues.StadiumsAsync(ct), Referees = await venues.RefereesAsync(ct),
        NewStadium = s ?? new StadiumInput(), NewReferee = r ?? new RefereeInput()
    };
}
