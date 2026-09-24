using Kokora.Application.Common;
using Kokora.Application.Live;
using Kokora.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kokora.Web.Areas.Admin.Controllers;

public record PeriodRequest(LivePeriod To);
public record MinuteRequest(int Minute);

/// <summary>
/// Mode terrain : écran de saisie en direct, pensé pour un téléphone au bord du terrain.
/// Les actions passent par une petite API JSON (file d'attente côté navigateur si le réseau coupe).
/// </summary>
[Route("admin/direct")]
public class DirectController(LiveMatchService live) : AdminController
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "direct";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) => View(await live.ListAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Match(int id, CancellationToken ct)
    {
        try
        {
            return View(await live.StateAsync(id, ct));
        }
        catch (BusinessRuleException ex)
        {
            FlashError(ex.Message);
            return Redirect("/admin/direct");
        }
    }

    [HttpGet("{id:int}/etat")]
    public Task<IActionResult> State(int id, CancellationToken ct) => Run(id, () => Task.CompletedTask, ct);

    [HttpPost("{id:int}/periode")]
    public Task<IActionResult> Period(int id, [FromBody] PeriodRequest request, CancellationToken ct) =>
        Run(id, () => live.AdvanceAsync(id, request.To, ct), ct);

    [HttpPost("{id:int}/actions")]
    public Task<IActionResult> AddEvent(int id, [FromBody] LiveEventInput input, CancellationToken ct) =>
        Run(id, () => live.AddEventAsync(id, input, ct), ct);

    [HttpPost("{id:int}/actions/{eventId:int}/annuler")]
    public Task<IActionResult> CancelEvent(int id, int eventId, CancellationToken ct) =>
        Run(id, () => live.CancelEventAsync(id, eventId, ct), ct);

    [HttpPost("{id:int}/minute")]
    public Task<IActionResult> Minute(int id, [FromBody] MinuteRequest request, CancellationToken ct) =>
        Run(id, () => live.SetMinuteAsync(id, request.Minute, ct), ct);

    /// <summary>Exécute l'action puis renvoie l'état à jour. 422 : action refusée (définitif) ; 409 : conflit (à renvoyer).</summary>
    private async Task<IActionResult> Run(int id, Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
            return Json(await live.StateAsync(id, ct));
        }
        catch (BusinessRuleException ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (LiveConflictException ex) { return Conflict(new { error = ex.Message }); }
        catch (NotFoundException) { return NotFound(new { error = "Match introuvable." }); }
    }
}
