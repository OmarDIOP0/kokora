using System.Security.Claims;
using Kokora.Application.Common;
using Kokora.Application.Engagement;
using Kokora.Application.Public;
using Kokora.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Controllers;

public record EndpointRequest(string Endpoint, List<int>? ClubIds);

/// <summary>Participation des supporters : notifications, pronostics, homme du match, commentaires.</summary>
public class EngagementController(NotificationService notifications, PredictionService predictions, VoteService votes,
    CommentService comments, MatchQueryService matches, Kokora.Application.Abstractions.IUserDirectory directory) : Controller
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsHtmx => Request.Headers.ContainsKey("HX-Request");

    // ------------------------------------------------------------ Notifications (sans compte)

    [HttpGet("/notifications/cle")]
    public IActionResult PublicKey() => notifications.PublicKey is { } k ? Json(new { key = k }) : NotFound();

    [HttpPost("/notifications/abonnement")]
    public async Task<IActionResult> Subscribe([FromBody] SubscriptionInput input, CancellationToken ct)
    {
        try
        {
            await notifications.SubscribeAsync(input, UserId, Request.Headers.UserAgent.ToString(), ct);
            return NoContent();
        }
        catch (BusinessRuleException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("/notifications/preferences")]
    public async Task<IActionResult> Preferences([FromBody] EndpointRequest request, CancellationToken ct) =>
        await notifications.PrefsAsync(request.Endpoint, ct) is { } p ? Json(p) : NotFound();

    [HttpPost("/notifications/equipes")]
    public async Task<IActionResult> Clubs([FromBody] EndpointRequest request, CancellationToken ct)
    {
        await notifications.UpdateClubsAsync(request.Endpoint, request.ClubIds ?? [], ct);
        return NoContent();
    }

    [HttpPost("/notifications/desabonnement")]
    public async Task<IActionResult> Unsubscribe([FromBody] EndpointRequest request, CancellationToken ct)
    {
        await notifications.UnsubscribeAsync(request.Endpoint, ct);
        return NoContent();
    }

    // ------------------------------------------------------------ Pronostics

    [HttpGet("/pronostics")]
    public async Task<IActionResult> Predictions(CancellationToken ct)
    {
        ViewData["Nav"] = "plus";
        var season = await matches.CurrentSeasonAsync(ct);
        if (season is null) return View(new PredictionsPageVm());
        return View(new PredictionsPageVm
        {
            Season = season,
            Upcoming = await predictions.UpcomingAsync(season.Id, UserId, ct),
            Board = await predictions.LeaderboardAsync(season.Id, UserId, 50, ct)
        });
    }

    [Authorize, HttpPost("/matchs/{id:int}/pronostic")]
    public async Task<IActionResult> Predict(int id, int home, int away, string? place, CancellationToken ct)
    {
        string? error = null;
        try { await predictions.SaveAsync(id, UserId!, home, away, ct); }
        catch (BusinessRuleException ex) { error = ex.Message; }
        if (!IsHtmx) return LocalRedirect(place == "liste" ? "/pronostics" : $"/matchs/{id}");
        if (place == "liste")
        {
            var row = await matches.RowAsync(id, ct);
            var block = await predictions.BlockAsync(id, UserId, ct);
            return PartialView("_PredictionRow", new PredictionRowVm(row!, block?.Mine, error, error is null));
        }
        return PartialView("_Prediction", new PredictionPartVm(id, await predictions.BlockAsync(id, UserId, ct), await matches.RowAsync(id, ct), error, error is null));
    }

    // ------------------------------------------------------------ Homme du match

    [Authorize, HttpPost("/matchs/{id:int}/vote")]
    public async Task<IActionResult> Vote(int id, int playerId, CancellationToken ct)
    {
        string? error = null;
        try { await votes.VoteAsync(id, UserId!, playerId, ct); }
        catch (BusinessRuleException ex) { error = ex.Message; }
        if (!IsHtmx) return Redirect($"/matchs/{id}");
        return PartialView("_Vote", new VotePartVm(id, await votes.BlockAsync(id, UserId, ct), error));
    }

    // ------------------------------------------------------------ Commentaires

    [Authorize, HttpPost("/infos/{id:int}/commentaires")]
    public async Task<IActionResult> Comment(int id, string body, CancellationToken ct)
    {
        string? message = null, error = null;
        try
        {
            var name = await directory.DisplayNameAsync(UserId!, ct) ?? User.Identity!.Name!;
            var status = await comments.PostAsync(id, UserId!, name, body, ct);
            message = status == Kokora.Domain.Enums.CommentStatus.Pending
                ? "Merci ! Votre commentaire sera visible après validation par l'équipe."
                : "Commentaire publié.";
        }
        catch (BusinessRuleException ex) { error = ex.Message; }
        if (!IsHtmx) return LocalRedirect("/infos");
        return PartialView("_Comments", new CommentsPartVm(id, true, await comments.ListAsync(id, UserId, ct), message, error, error is null ? null : body));
    }

    [Authorize, HttpPost("/commentaires/{id:int}/supprimer")]
    public async Task<IActionResult> DeleteComment(int id, int articleId, CancellationToken ct)
    {
        try { await comments.DeleteOwnAsync(id, UserId!, ct); } catch (NotFoundException) { }
        if (!IsHtmx) return Redirect("/infos");
        return PartialView("_Comments", new CommentsPartVm(articleId, true, await comments.ListAsync(articleId, UserId, ct), "Commentaire supprimé.", null, null));
    }
}
