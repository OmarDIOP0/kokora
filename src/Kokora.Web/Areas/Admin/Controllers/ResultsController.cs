using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Domain.Matches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Controllers;

public record SquadPlayer(int Id, string Name, int? Number);

public class ResultPageVm
{
    public required ResultInput Input { get; init; }
    public required Match Match { get; init; }
    public required Dictionary<int, List<SquadPlayer>> Squads { get; init; }
}

/// <summary>Saisie rapide du résultat d'un match (score, buts, cartons, forfait, tirs au but).</summary>
[Route("admin/matchs/{id:int}/resultat")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class ResultsController(ResultService results, IAppDbContext db) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "matchs";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var m = await results.GetAsync(id, ct);
        if (m.HomeClubId is null || m.AwayClubId is null)
        {
            FlashError("Désignez d'abord les deux équipes du match.");
            return Redirect($"/admin/matchs/{id}/modifier");
        }
        return View(await Page(m, ResultService.ToInput(m), ct));
    }

    [HttpPost("")]
    public async Task<IActionResult> Save(int id, ResultInput input, CancellationToken ct)
    {
        input.MatchId = id;
        if (ModelState.IsValid)
        {
            try
            {
                await results.SaveAsync(input, ct);
                Flash("Résultat enregistré. Classements mis à jour.");
                return Redirect("/admin/matchs?sansResultat=true");
            }
            catch (BusinessRuleException ex) { AddError(ex); }
        }
        return View("Edit", await Page(await results.GetAsync(id, ct), input, ct));
    }

    [HttpPost("effacer")]
    public async Task<IActionResult> Clear(int id, CancellationToken ct)
    {
        await results.ClearAsync(id, ct);
        Flash("Résultat effacé : le match est de nouveau programmé.");
        return Redirect($"/admin/matchs/{id}/resultat");
    }

    private async Task<ResultPageVm> Page(Match m, ResultInput input, CancellationToken ct)
    {
        var seasonId = m.Phase.Competition.SeasonId;
        var clubIds = new[] { m.HomeClubId!.Value, m.AwayClubId!.Value };
        var squads = await db.SquadMembers.AsNoTracking()
            .Where(s => s.SeasonId == seasonId && clubIds.Contains(s.ClubId))
            .OrderBy(s => s.ShirtNumber == null).ThenBy(s => s.ShirtNumber).ThenBy(s => s.Player.LastName)
            .Select(s => new { s.ClubId, s.PlayerId, s.Player.FirstName, s.Player.LastName, s.Player.Nickname, s.ShirtNumber })
            .ToListAsync(ct);
        return new ResultPageVm
        {
            Input = input, Match = m,
            Squads = clubIds.ToDictionary(c => c, c => squads.Where(s => s.ClubId == c)
                .Select(s => new SquadPlayer(s.PlayerId, s.Nickname ?? $"{s.FirstName} {s.LastName}", s.ShirtNumber)).ToList())
        };
    }
}
