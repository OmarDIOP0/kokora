using Kokora.Application.Admin;
using Kokora.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kokora.Web.Areas.Admin.Controllers;

[Route("admin")]
public class DashboardController(DashboardService dashboard) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["AdminNav"] = "dashboard";
        return View(await dashboard.GetAsync(ct));
    }

    [HttpGet("audit")]
    [Authorize(Roles = Roles.AdminOrAbove)]
    public async Task<IActionResult> Audit(string? type, string? user, DateOnly? day, int page = 1, CancellationToken ct = default)
    {
        ViewData["AdminNav"] = "audit";
        var filter = new AuditFilter(type, user, day, Math.Max(1, page));
        var (items, total) = await dashboard.AuditAsync(filter, ct);
        return View(new Models.AuditPageVm
        {
            Items = items, Total = total, Filter = filter, EntityTypes = await dashboard.AuditEntityTypesAsync(ct)
        });
    }
}
