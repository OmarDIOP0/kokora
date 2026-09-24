using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Kokora.Web.Infrastructure;

/// <summary>
/// Formulaire expiré (jeton anti-CSRF invalide : page restée ouverte pendant un redémarrage du serveur, session changée…).
/// Plutôt qu'une page d'erreur vide, on revient sur la page du formulaire avec un message : il suffit de recommencer.
/// Les appels htmx et JSON gardent une erreur 400 (message texte, affiché par la page).
/// </summary>
public class ExpiredFormFilter(ITempDataDictionaryFactory tempData) : IAlwaysRunResultFilter
{
    public const string Key = "k-expired";
    public const string Message = "La page avait expiré (le site a peut-être redémarré). Recommencez : c'est en ordre maintenant.";

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not IAntiforgeryValidationFailedResult) return;
        var http = context.HttpContext;
        var request = http.Request;
        if (request.Headers.ContainsKey("HX-Request") || !request.HasFormContentType)
        {
            context.Result = new ContentResult { StatusCode = 400, Content = Message, ContentType = "text/plain; charset=utf-8" };
            return;
        }
        var target = "/";
        if (Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer) && referer.Host == request.Host.Host)
            target = referer.PathAndQuery;
        var data = tempData.GetTempData(http);
        data[Key] = Message;
        data["k-flash"] = System.Text.Json.JsonSerializer.Serialize(new { type = "error", message = Message }); // toast de l'admin
        data.Save(); // le filtre d'enregistrement de TempData ne s'exécute pas quand l'autorisation court-circuite la requête
        context.Result = new LocalRedirectResult(target);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
