using System.Text.Json;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kokora.Web.Areas.Admin;

/// <summary>
/// Base des contrôleurs d'administration : messages flash, erreurs métier.
/// Accès minimal : équipe (Staff). Les contrôleurs sportifs exigent en plus Admin ou SuperAdmin.
/// </summary>
[Area("Admin")]
[Authorize(Roles = Roles.Staff)]
public abstract class AdminController : Controller
{
    protected const string FlashKey = "k-flash";

    protected void Flash(string message, string type = "success") =>
        TempData[FlashKey] = JsonSerializer.Serialize(new { type, message });

    protected void FlashError(string message) => Flash(message, "error");

    /// <summary>Ajoute l'erreur métier au formulaire (sur le bon champ si possible).</summary>
    protected void AddError(BusinessRuleException ex, string? prefix = null)
    {
        var key = ex.Field is null ? "" : prefix is null ? ex.Field : $"{prefix}.{ex.Field}";
        ModelState.AddModelError(key, ex.Message);
    }

    protected bool IsHtmx => Request.Headers.ContainsKey("HX-Request");

    /// <summary>Retour à la page précédente (même site uniquement), sinon vers <paramref name="fallback"/>.</summary>
    protected IActionResult Back(string fallback)
    {
        var referer = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == Request.Host.Host)
            return LocalRedirect(uri.PathAndQuery);
        return LocalRedirect(fallback);
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        // Erreur métier non gérée par l'action (ex. suppression refusée) : message + retour arrière.
        if (context.Exception is BusinessRuleException ex && !context.ExceptionHandled)
        {
            FlashError(ex.Message);
            context.Result = IsHtmx
                ? new ContentResult { StatusCode = 422, Content = ex.Message, ContentType = "text/plain; charset=utf-8" }
                : Back("/admin");
            context.ExceptionHandled = true;
        }
        else if (context.Exception is NotFoundException && !context.ExceptionHandled)
        {
            context.Result = NotFound();
            context.ExceptionHandled = true;
        }
        base.OnActionExecuted(context);
    }
}
