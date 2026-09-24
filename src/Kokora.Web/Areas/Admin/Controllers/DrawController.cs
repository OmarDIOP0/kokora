using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Areas.Admin.Controllers;

public class DrawPageVm
{
    public string? SeasonName { get; init; }
    public string Text { get; init; } = "";
    public DrawReport? Report { get; init; }
    public string? Error { get; init; }
}

/// <summary>Import du tirage au sort des poules (saison de travail), puis remplissage fictif pour tester.</summary>
[Route("admin/tirage")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class DrawController(DrawImportService draws, DemoDataService demo, AdminSeason season) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "saisons";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new DrawPageVm { SeasonName = (await season.GetAsync(ct))?.Name });

    [HttpPost("")]
    public async Task<IActionResult> Import(string text, CancellationToken ct)
    {
        var working = await season.GetAsync(ct);
        if (working is null) return Redirect("/admin/saisons");
        try
        {
            var report = await draws.ImportAsync(working.Value.Id, text, ct);
            Flash($"Tirage importé : {report.Groups} poule(s), {report.ClubsCreated} équipe(s) créée(s).");
            return View("Index", new DrawPageVm { SeasonName = working.Value.Name, Text = text, Report = report });
        }
        catch (BusinessRuleException ex)
        {
            return View("Index", new DrawPageVm { SeasonName = working.Value.Name, Text = text, Error = ex.Message });
        }
    }

    [HttpPost("fictif")]
    public async Task<IActionResult> Fill(CancellationToken ct)
    {
        var working = await season.GetAsync(ct);
        if (working is null) return Redirect("/admin/saisons");
        var r = await demo.FillSeasonAsync(working.Value.Id, ct);
        Flash($"Données fictives ajoutées : {r.Players} joueurs, {r.Matches} matchs dont {r.Played} joués.");
        return Redirect("/admin/tirage");
    }
}
