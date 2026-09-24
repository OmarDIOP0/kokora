using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Controllers;

public class DisciplinePageVm
{
    public List<SuspensionRowVm> Active { get; init; } = [];
    public List<ManualSuspensionItem> Manual { get; init; } = [];
    public List<PointAdjustmentItem> Adjustments { get; init; } = [];
    public SuspensionInput NewSuspension { get; init; } = new();
    public PointAdjustmentInput NewAdjustment { get; init; } = new();
    public List<SelectListItem> Players { get; init; } = [];
    public List<SelectListItem> Competitions { get; init; } = [];
    public List<SelectListItem> Groups { get; init; } = [];
    public List<SelectListItem> Clubs { get; init; } = [];
}

/// <summary>Sanctions : suspensions (automatiques + commission) et pénalités de points.</summary>
[Route("admin/discipline")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class DisciplineController(DisciplineAdminService discipline, StatsService stats, AdminSeason season,
    Models.Lookups lookups, IAppDbContext db) : AdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "discipline";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var s = await season.GetAsync(ct);
        if (s is null) return Redirect("/admin/saisons/nouvelle");
        return View(await Page(s.Value.Id, null, null, ct));
    }

    [HttpPost("suspensions")]
    public async Task<IActionResult> AddSuspension([Bind(Prefix = "NewSuspension")] SuspensionInput input, CancellationToken ct)
    {
        var s = (await season.GetAsync(ct))!.Value;
        if (ModelState.IsValid)
        {
            try
            {
                await discipline.AddSuspensionAsync(input, s.Id, ct);
                Flash("Suspension enregistrée.");
                return Redirect("/admin/discipline");
            }
            catch (BusinessRuleException ex) { AddError(ex, "NewSuspension"); }
        }
        return View("Index", await Page(s.Id, input, null, ct));
    }

    [HttpPost("suspensions/{id:int}/supprimer")]
    public async Task<IActionResult> DeleteSuspension(int id, CancellationToken ct)
    {
        await discipline.DeleteSuspensionAsync(id, ct);
        Flash("Suspension supprimée.");
        return Redirect("/admin/discipline");
    }

    [HttpPost("penalites")]
    public async Task<IActionResult> AddAdjustment([Bind(Prefix = "NewAdjustment")] PointAdjustmentInput input, CancellationToken ct)
    {
        var s = (await season.GetAsync(ct))!.Value;
        if (ModelState.IsValid)
        {
            try
            {
                await discipline.AddAdjustmentAsync(input, ct);
                Flash("Décision enregistrée : le classement est recalculé.");
                return Redirect("/admin/discipline");
            }
            catch (BusinessRuleException ex) { AddError(ex, "NewAdjustment"); }
        }
        return View("Index", await Page(s.Id, null, input, ct));
    }

    [HttpPost("penalites/{id:int}/supprimer")]
    public async Task<IActionResult> DeleteAdjustment(int id, CancellationToken ct)
    {
        await discipline.DeleteAdjustmentAsync(id, ct);
        Flash("Pénalité annulée : le classement est recalculé.");
        return Redirect("/admin/discipline");
    }

    private async Task<DisciplinePageVm> Page(int seasonId, SuspensionInput? suspension, PointAdjustmentInput? adjustment, CancellationToken ct)
    {
        var comps = await db.Competitions.AsNoTracking().Where(c => c.SeasonId == seasonId).OrderBy(c => c.Order).ToListAsync(ct);
        var (active, _) = await stats.DisciplineAsync(comps.Select(c => (c.Id, c.Name, c.Suspensions)).ToList(), ct);
        var players = await db.SquadMembers.AsNoTracking().Where(m => m.SeasonId == seasonId)
            .OrderBy(m => m.Club.Name).ThenBy(m => m.Player.LastName)
            .Select(m => new { m.PlayerId, Name = m.Player.FirstName + " " + m.Player.LastName, m.ShirtNumber, Club = m.Club.Name })
            .ToListAsync(ct);
        var groupsByClub = new Dictionary<string, SelectListGroup>();
        var groups = await db.Groups.AsNoTracking().Where(g => g.Phase.Competition.SeasonId == seasonId)
            .OrderBy(g => g.Phase.Competition.Order).ThenBy(g => g.Phase.Order).ThenBy(g => g.Order)
            .Select(g => new { g.Id, Label = g.Phase.Competition.Name + " · " + g.Phase.Name + " · " + g.Name }).ToListAsync(ct);

        return new DisciplinePageVm
        {
            Active = active,
            Manual = await discipline.SuspensionsAsync(seasonId, ct),
            Adjustments = await discipline.AdjustmentsAsync(seasonId, ct),
            NewSuspension = suspension ?? new SuspensionInput { DecidedOn = KokoraTime.Today },
            NewAdjustment = adjustment ?? new PointAdjustmentInput { DecidedOn = KokoraTime.Today },
            Players = players.Select(p =>
            {
                if (!groupsByClub.TryGetValue(p.Club, out var g)) groupsByClub[p.Club] = g = new SelectListGroup { Name = p.Club };
                return new SelectListItem(p.ShirtNumber is { } n ? $"{n}. {p.Name}" : p.Name, p.PlayerId.ToString()) { Group = g };
            }).ToList(),
            Competitions = comps.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList(),
            Groups = groups.Select(g => new SelectListItem(g.Label, g.Id.ToString())).ToList(),
            Clubs = await lookups.ClubsAsync(onlyActive: false, ct)
        };
    }
}
