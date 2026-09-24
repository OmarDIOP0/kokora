using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class MatchesController(ScheduleService schedule, AdminSeason season, Lookups lookups, IAppDbContext db) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "matchs";
        base.OnActionExecuting(context);
    }

    [HttpGet("matchs")]
    public async Task<IActionResult> Index(int? competition, int? equipe, DateOnly? jour, bool sansResultat = false, CancellationToken ct = default)
    {
        var s = await season.GetAsync(ct);
        if (s is null) return Redirect("/admin/saisons/nouvelle");
        var items = await schedule.ListAsync(new MatchFilter(s.Value.Id, competition, null, jour, sansResultat, equipe), ct);
        var vm = new MatchesPageVm
        {
            Items = items, CompetitionId = competition, ClubId = equipe, Day = jour, Missing = sansResultat,
            Competitions = IsHtmx ? [] : await lookups.CompetitionsAsync(s.Value.Id, ct),
            Clubs = IsHtmx ? [] : await lookups.ClubsAsync(onlyActive: false, ct)
        };
        return IsHtmx ? PartialView("_List", vm) : View(vm);
    }

    [HttpGet("matchs/nouveau")]
    public async Task<IActionResult> Create(int? phaseId, CancellationToken ct)
    {
        var s = await season.GetAsync(ct);
        if (s is null) return Redirect("/admin/saisons/nouvelle");
        var input = new MatchInput { PhaseId = phaseId ?? 0, KickoffLocal = KokoraTime.Today.ToDateTime(new TimeOnly(16, 30)) };
        return View("Form", await Form(input, s.Value.Id, ct));
    }

    [HttpGet("matchs/{id:int}/modifier")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var m = await schedule.GetAsync(id, ct);
        var vm = await Form(ScheduleService.ToInput(m), m.Phase.Competition.SeasonId, ct);
        vm.Status = m.Status;
        return View("Form", vm);
    }

    [HttpPost("matchs/enregistrer")]
    public async Task<IActionResult> Save([Bind(Prefix = "Input")] MatchInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await schedule.SaveAsync(input, ct);
                Flash(input.Id == 0 ? "Match programmé." : "Match enregistré.");
                return Redirect("/admin/matchs");
            }
            catch (BusinessRuleException ex) { AddError(ex, "Input"); }
        }
        var s = await season.GetAsync(ct);
        var vm = await Form(input, s!.Value.Id, ct);
        if (input.Id != 0) vm.Status = (await schedule.GetAsync(input.Id, ct)).Status;
        return View("Form", vm);
    }

    [HttpPost("matchs/{id:int}/reporter")]
    public async Task<IActionResult> Postpone(int id, DateTime? nouvelleDate, CancellationToken ct)
    {
        await schedule.PostponeAsync(id, nouvelleDate, ct);
        Flash(nouvelleDate is null ? "Match reporté (date à fixer)." : "Match reprogrammé.");
        return Back("/admin/matchs");
    }

    [HttpPost("matchs/{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await schedule.DeleteAsync(id, ct);
        Flash("Match supprimé.");
        return Redirect("/admin/matchs");
    }

    // ------------------------------------------------------------ Génération du calendrier d'une poule

    [HttpGet("poules/{id:int}/calendrier")]
    public async Task<IActionResult> Generate(int id, CancellationToken ct) =>
        View(await GenerateVm(new FixtureGenerationInput { GroupId = id, FirstDate = NextSaturday() }, null, ct));

    [HttpPost("poules/{id:int}/calendrier/apercu")]
    public async Task<IActionResult> Preview(int id, [Bind(Prefix = "Input")] FixtureGenerationInput input, CancellationToken ct)
    {
        input.GroupId = id;
        List<ProposedFixture>? preview = null;
        if (ModelState.IsValid)
        {
            try { preview = await schedule.PreviewGroupFixturesAsync(input, ct); }
            catch (BusinessRuleException ex) { AddError(ex, "Input"); }
        }
        var vm = await GenerateVm(input, preview, ct);
        return IsHtmx ? PartialView("_FixturePreview", vm) : View("Generate", vm);
    }

    [HttpPost("poules/{id:int}/calendrier")]
    public async Task<IActionResult> GenerateConfirm(int id, [Bind(Prefix = "Input")] FixtureGenerationInput input, CancellationToken ct)
    {
        input.GroupId = id;
        if (ModelState.IsValid)
        {
            try
            {
                var count = await schedule.GenerateGroupFixturesAsync(input, ct);
                var phaseId = await db.Groups.Where(g => g.Id == id).Select(g => g.PhaseId).FirstAsync(ct);
                Flash($"{count} matchs créés.");
                return Redirect($"/admin/phases/{phaseId}");
            }
            catch (BusinessRuleException ex) { AddError(ex, "Input"); }
        }
        return View("Generate", await GenerateVm(input, null, ct));
    }

    private async Task<GenerateFixturesVm> GenerateVm(FixtureGenerationInput input, List<ProposedFixture>? preview, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking().Include(g => g.Teams).ThenInclude(t => t.Club)
            .Include(g => g.Phase).ThenInclude(p => p.Competition)
            .FirstOrDefaultAsync(g => g.Id == input.GroupId, ct) ?? throw new NotFoundException("Poule");
        return new GenerateFixturesVm
        {
            Input = input, Group = group, Preview = preview, Stadiums = await lookups.StadiumsAsync(ct),
            ExistingMatches = await db.Matches.CountAsync(m => m.GroupId == group.Id, ct)
        };
    }

    private async Task<MatchFormVm> Form(MatchInput input, int seasonId, CancellationToken ct)
    {
        var (phases, groups, rounds) = await lookups.MatchContextAsync(seasonId, ct);
        if (input.PhaseId == 0 && phases.Count > 0) input.PhaseId = phases[0].Id;
        return new MatchFormVm
        {
            Input = input, Phases = phases, Groups = groups, Rounds = rounds,
            Clubs = await lookups.ClubsAsync(ct: ct), Stadiums = await lookups.StadiumsAsync(ct), Referees = await lookups.RefereesAsync(ct)
        };
    }

    private static DateOnly NextSaturday()
    {
        var d = KokoraTime.Today;
        while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
        return d;
    }
}
