using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        ViewData["Nav"] = "matchs";
        return View();
    }

    // Toutes méthodes : la page d'erreur est rejouée avec la méthode de la requête d'origine (ex. POST).
    [Route("/erreur")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["RequestId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }
}
