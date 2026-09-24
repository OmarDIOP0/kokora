using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin/demo")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class DemoController(DemoDataService demo, AdminSeason season, Kokora.Application.Abstractions.IAppDbContext db) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["AdminNav"] = "demo";
        return View(await demo.CountAsync(ct));
    }

    [HttpPost("creer")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        await demo.SeedAsync(ct);
        var demoSeason = db.Seasons.Where(s => s.IsDemo).Select(s => s.Id).FirstOrDefault();
        if (demoSeason != 0) season.Set(demoSeason);
        Flash("Données de démonstration créées : 17 ASC fictives, 238 joueurs, 2 zonales en cours (poules de 4 et 5).");
        return Redirect("/admin/demo");
    }

    [HttpPost("supprimer")]
    public async Task<IActionResult> Purge(CancellationToken ct)
    {
        await demo.PurgeAsync(ct);
        Response.Cookies.Delete(AdminSeason.Cookie);
        Flash("Toutes les données de démonstration ont été supprimées.");
        return Redirect("/admin/demo");
    }
}
