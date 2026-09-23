using Kokora.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Controllers;

/// <summary>Design system et maquettes (données fictives). Visible en développement ou pour les administrateurs.</summary>
[Route("design")]
public class DesignController(IWebHostEnvironment env) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => Allowed() ? View() : NotFound();

    [HttpGet("accueil")]
    public IActionResult Home() => Allowed() ? View() : NotFound();

    private bool Allowed() =>
        env.IsDevelopment() || User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Admin);
}
