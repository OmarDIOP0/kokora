using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Kokora.Web.Areas.Admin.Controllers;

public class QuickResultsPageVm
{
    public string Tab { get; init; } = "resultats";
    public int? CompetitionId { get; init; }
    public List<SelectListItem> Competitions { get; init; } = [];
    public List<QuickMatchVm> Pending { get; init; } = [];
    public List<QuickMatchVm> WithoutMotm { get; init; } = [];
    /// <summary>Valeurs saisies (réaffichées si une ligne est refusée).</summary>
    public Dictionary<int, QuickResultRow> Values { get; init; } = [];
    public Dictionary<int, string> Errors { get; init; } = [];
}

/// <summary>Saisie rapide des résultats et désignation des hommes du match (saison de travail).</summary>
[Route("admin/resultats")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class QuickResultsController(QuickResultService quick, AdminSeason season, Lookups lookups) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "matchs";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? onglet, int? competition, CancellationToken ct) =>
        View(await PageAsync(onglet ?? "resultats", competition, [], [], ct));

    [HttpPost("")]
    public async Task<IActionResult> Save(List<QuickResultRow> rows, int? competition, CancellationToken ct)
    {
        var report = await quick.SaveAsync(rows, ct);
        if (report.Saved > 0) Flash(report.Errors.Count == 0
            ? $"{report.Saved} résultat(s) enregistré(s). Classements mis à jour."
            : $"{report.Saved} résultat(s) enregistré(s) ; {report.Errors.Count} à corriger ci-dessous.");
        if (report.Errors.Count == 0) return Redirect($"/admin/resultats{(competition is null ? "" : $"?competition={competition}")}");
        return View("Index", await PageAsync("resultats", competition, rows.ToDictionary(r => r.MatchId), report.Errors, ct));
    }

    [HttpPost("hommes-du-match")]
    public async Task<IActionResult> SaveMotm(List<QuickResultRow> rows, int? competition, CancellationToken ct)
    {
        var report = await quick.SaveManOfTheMatchAsync(rows, ct);
        if (report.Saved > 0) Flash($"{report.Saved} homme(s) du match désigné(s).");
        if (report.Errors.Count == 0)
            return Redirect($"/admin/resultats?onglet=hommes-du-match{(competition is null ? "" : $"&competition={competition}")}");
        return View("Index", await PageAsync("hommes-du-match", competition, rows.ToDictionary(r => r.MatchId), report.Errors, ct));
    }

    private async Task<QuickResultsPageVm> PageAsync(string tab, int? competitionId, Dictionary<int, QuickResultRow> values,
        Dictionary<int, string> errors, CancellationToken ct)
    {
        var working = await season.GetAsync(ct);
        if (working is null) return new QuickResultsPageVm { Tab = tab };
        return new QuickResultsPageVm
        {
            Tab = tab, CompetitionId = competitionId,
            Competitions = await lookups.CompetitionsAsync(working.Value.Id, ct),
            Pending = await quick.PendingAsync(working.Value.Id, competitionId, ct),
            WithoutMotm = await quick.WithoutManOfTheMatchAsync(working.Value.Id, competitionId, ct),
            Values = values, Errors = errors
        };
    }
}
