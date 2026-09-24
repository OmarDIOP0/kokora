using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = Kokora.Application.Abstractions.Roles.AdminOrAbove)]
public class CompetitionsController(CompetitionAdminService competitions, SeasonAdminService seasons,
    ScheduleService schedule, QualificationService qualifications, Lookups lookups, IAppDbContext db) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "saisons";
        base.OnActionExecuting(context);
    }

    // ------------------------------------------------------------ Compétitions

    [HttpGet("saisons/{seasonId:int}/competitions/nouvelle")]
    public async Task<IActionResult> Create(int seasonId, CancellationToken ct)
    {
        var season = await seasons.GetAsync(seasonId, ct);
        return View("Form", new CompetitionFormVm { Input = new CompetitionInput { SeasonId = seasonId }, SeasonName = season.Name });
    }

    [HttpGet("competitions/{id:int}/modifier")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var c = await competitions.GetAsync(id, ct);
        return View("Form", new CompetitionFormVm { Input = CompetitionAdminService.ToInput(c), SeasonName = c.Season.Name });
    }

    [HttpPost("competitions/enregistrer")]
    public async Task<IActionResult> Save([Bind(Prefix = "Input")] CompetitionInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await competitions.SaveAsync(input, ct);
                Flash("Compétition enregistrée.");
                return Redirect($"/admin/competitions/{id}");
            }
            catch (BusinessRuleException ex) { AddError(ex, "Input"); }
        }
        var season = await seasons.GetAsync(input.SeasonId, ct);
        return View("Form", new CompetitionFormVm { Input = input, SeasonName = season.Name });
    }

    [HttpGet("competitions/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct) => View(await competitions.GetAsync(id, ct));

    [HttpPost("competitions/{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var c = await competitions.GetAsync(id, ct);
        await competitions.DeleteAsync(id, ct);
        Flash($"« {c.Name} » supprimée.");
        return Redirect("/admin/saisons");
    }

    [HttpPost("saisons/{seasonId:int}/competitions/ordre")]
    public async Task<IActionResult> Reorder(int seasonId, int[] ids, CancellationToken ct)
    {
        await competitions.ReorderAsync(seasonId, ids, ct);
        return NoContent();
    }

    // ------------------------------------------------------------ Phases

    [HttpGet("competitions/{competitionId:int}/phases/nouvelle")]
    public async Task<IActionResult> CreatePhase(int competitionId, CancellationToken ct)
    {
        var c = await competitions.GetAsync(competitionId, ct);
        ViewData["Competition"] = c;
        return View("PhaseForm", new PhaseInput { CompetitionId = competitionId });
    }

    [HttpGet("phases/{id:int}/modifier")]
    public async Task<IActionResult> EditPhase(int id, CancellationToken ct)
    {
        var p = await competitions.GetPhaseAsync(id, ct);
        ViewData["Competition"] = p.Competition;
        return View("PhaseForm", new PhaseInput
        {
            Id = p.Id, CompetitionId = p.CompetitionId, Name = p.Name, Type = p.Type, Legs = p.Legs,
            HasExtraTime = p.HasExtraTime, HasPenalties = p.HasPenalties
        });
    }

    [HttpPost("phases/enregistrer")]
    public async Task<IActionResult> SavePhase(PhaseInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await competitions.SavePhaseAsync(input, ct);
                Flash("Phase enregistrée.");
                return Redirect($"/admin/phases/{id}");
            }
            catch (BusinessRuleException ex) { AddError(ex); }
        }
        ViewData["Competition"] = await competitions.GetAsync(input.CompetitionId, ct);
        return View("PhaseForm", input);
    }

    [HttpGet("phases/{id:int}")]
    public async Task<IActionResult> Phase(int id, CancellationToken ct) => View(await BuildPhasePage(id, null, ct));

    [HttpPost("phases/{id:int}/supprimer")]
    public async Task<IActionResult> DeletePhase(int id, CancellationToken ct)
    {
        var p = await competitions.GetPhaseAsync(id, ct);
        await competitions.DeletePhaseAsync(id, ct);
        Flash("Phase supprimée.");
        return Redirect($"/admin/competitions/{p.CompetitionId}");
    }

    [HttpPost("competitions/{competitionId:int}/phases/ordre")]
    public async Task<IActionResult> ReorderPhases(int competitionId, int[] ids, CancellationToken ct)
    {
        await competitions.ReorderPhasesAsync(competitionId, ids, ct);
        return NoContent();
    }

    // ------------------------------------------------------------ Poules

    [HttpGet("poules/{id:int}/modifier")]
    public async Task<IActionResult> EditGroup(int id, CancellationToken ct)
    {
        var group = await db.Groups.Include(g => g.Teams).Include(g => g.Phase).ThenInclude(p => p.Competition)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return NotFound();
        ViewData["Phase"] = group.Phase;
        ViewData["Clubs"] = await lookups.ClubsAsync(ct: ct);
        return View("GroupForm", CompetitionAdminService.ToInput(group));
    }

    [HttpPost("poules/enregistrer")]
    public async Task<IActionResult> SaveGroup(GroupInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await competitions.SaveGroupAsync(input, ct);
                Flash(input.Id == 0 ? "Poule créée." : "Poule enregistrée.");
                return Redirect($"/admin/phases/{input.PhaseId}");
            }
            catch (BusinessRuleException ex) { AddError(ex); }
        }
        if (input.Id == 0) return View("Phase", await BuildPhasePage(input.PhaseId, input, ct));
        ViewData["Phase"] = await competitions.GetPhaseAsync(input.PhaseId, ct);
        ViewData["Clubs"] = await lookups.ClubsAsync(ct: ct);
        return View("GroupForm", input);
    }

    [HttpPost("poules/{id:int}/supprimer")]
    public async Task<IActionResult> DeleteGroup(int id, int phaseId, CancellationToken ct)
    {
        await competitions.DeleteGroupAsync(id, ct);
        Flash("Poule supprimée.");
        return Redirect($"/admin/phases/{phaseId}");
    }

    // ------------------------------------------------------------ Tableau à élimination

    [HttpPost("phases/{id:int}/tableau")]
    public async Task<IActionResult> CreateBracket(int id, KnockoutInput input, CancellationToken ct)
    {
        input.PhaseId = id;
        await competitions.CreateKnockoutBracketAsync(input, ct);
        Flash("Tableau créé. Désignez les équipes du premier tour dans le calendrier.");
        return Redirect($"/admin/phases/{id}");
    }

    [HttpPost("phases/{id:int}/tableau/supprimer")]
    public async Task<IActionResult> ClearBracket(int id, CancellationToken ct)
    {
        await competitions.ClearKnockoutBracketAsync(id, ct);
        Flash("Tableau supprimé.");
        return Redirect($"/admin/phases/{id}");
    }

    // ------------------------------------------------------------ Qualifications

    /// <summary>Sources des places du premier tour : clés « {matchId}:{Home|Away} » → « g12:1 », « w45 », « l45 » ou vide.</summary>
    [HttpPost("phases/{id:int}/qualifications")]
    public async Task<IActionResult> SaveQualifications(int id, Dictionary<string, string?> sources, CancellationToken ct)
    {
        foreach (var (key, value) in sources)
        {
            if (key.Split(':') is not [var matchText, var slotText] || !int.TryParse(matchText, out var matchId)
                || !Enum.TryParse<Kokora.Domain.Enums.MatchSlot>(slotText, out var slot)) continue;
            await qualifications.SetSourceAsync(matchId, slot, value, ct);
        }
        Flash("Règles de qualification enregistrées.");
        return Redirect($"/admin/phases/{id}#qualification");
    }

    [HttpPost("phases/{id:int}/qualifications/generer")]
    public async Task<IActionResult> GenerateQualifications(int id, CancellationToken ct)
    {
        var filled = await qualifications.GenerateAsync(id, ct);
        Flash(filled == 0
            ? "Aucune équipe placée : les poules ou matchs sources ne sont pas encore terminés."
            : $"{filled} place{(filled > 1 ? "s" : "")} remplie{(filled > 1 ? "s" : "")} d'après les classements et résultats actuels.");
        return Redirect($"/admin/phases/{id}#qualification");
    }

    private async Task<PhasePageVm> BuildPhasePage(int id, GroupInput? newGroup, CancellationToken ct)
    {
        var phase = await competitions.GetPhaseAsync(id, ct);
        var counts = await db.Matches.Where(m => m.PhaseId == id && m.GroupId != null)
            .GroupBy(m => m.GroupId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var bracket = phase.Type == PhaseType.Knockout
            ? await schedule.ListAsync(new MatchFilter(phase.Competition.SeasonId, PhaseId: id), ct)
            : [];
        return new PhasePageVm
        {
            Phase = phase,
            Clubs = await lookups.ClubsAsync(ct: ct),
            NewGroup = newGroup ?? new GroupInput { PhaseId = id, Name = $"Poule {(char)('A' + phase.Groups.Count)}" },
            Knockout = new KnockoutInput { PhaseId = id },
            BracketMatches = bracket,
            MatchCountByGroup = counts,
            Slots = phase.Type == PhaseType.Knockout ? await qualifications.SlotsAsync(id, ct) : [],
            Sources = phase.Type == PhaseType.Knockout ? await qualifications.SourcesAsync(id, ct) : []
        };
    }
}
