using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Application.Engagement;
using Kokora.Domain.Enums;
using Kokora.Infrastructure.Identity;
using Kokora.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Controllers;

/// <summary>Modération des commentaires (équipe de rédaction comprise).</summary>
[Route("admin/commentaires")]
public class CommentsController(CommentService comments) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? statut, CancellationToken ct)
    {
        ViewData["AdminNav"] = "commentaires";
        var status = statut switch { "publies" => CommentStatus.Approved, "refuses" => CommentStatus.Rejected, _ => CommentStatus.Pending };
        return View(new CommentsPageVm(status, await comments.ModerationAsync(status, ct: ct), await comments.PendingCountAsync(ct)));
    }

    [HttpPost("{id:int}/statut")]
    public async Task<IActionResult> SetStatus(int id, CommentStatus status, CancellationToken ct)
    {
        await comments.SetStatusAsync(id, status, ct);
        if (IsHtmx) return Content("");
        Flash(status == CommentStatus.Approved ? "Commentaire publié." : "Commentaire refusé.");
        return Back("/admin/commentaires");
    }

    [HttpPost("{id:int}/supprimer")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await comments.DeleteAsync(id, ct);
        if (IsHtmx) return Content("");
        Flash("Commentaire supprimé.");
        return Back("/admin/commentaires");
    }
}

/// <summary>Comptes et rôles. Les admins gèrent rédacteurs et supporters ; seul un SuperAdmin nomme des admins.</summary>
[Route("admin/utilisateurs")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class UsersController(UserManager<AppUser> users) : AdminController
{
    public const int PageSize = 50;

    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        ViewData["AdminNav"] = "utilisateurs";
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, string? role, CancellationToken ct)
    {
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLower();
            query = query.Where(u => u.DisplayName.ToLower().Contains(s) || (u.UserName ?? "").ToLower().Contains(s)
                || (u.Email ?? "").ToLower().Contains(s) || (u.PhoneNumber ?? "").Contains(s));
        }
        if (!string.IsNullOrEmpty(role))
        {
            var inRole = (await users.GetUsersInRoleAsync(role)).Select(u => u.Id).ToList();
            query = query.Where(u => inRole.Contains(u.Id));
        }
        var total = await query.CountAsync(ct);
        var list = await query.OrderByDescending(u => u.CreatedAt).Take(PageSize).ToListAsync(ct);
        var items = new List<UserItem>();
        foreach (var u in list)
            items.Add(new UserItem(u.Id, u.DisplayName, u.Email ?? u.PhoneNumber ?? u.UserName ?? "", (await users.GetRolesAsync(u)).FirstOrDefault() ?? Roles.User,
                u.CreatedAt, u.LastSeenAt, u.LockoutEnd > DateTimeOffset.UtcNow));
        return View(new UsersPageVm(items, total, q, role, AssignableRoles()));
    }

    private List<SelectListItem> AssignableRoles() =>
        (User.IsInRole(Roles.SuperAdmin) ? Roles.All : [Roles.Editor, Roles.User])
            .Select(r => new SelectListItem(RoleLabel(r), r)).ToList();

    public static string RoleLabel(string role) => role switch
    {
        Roles.SuperAdmin => "Super admin", Roles.Admin => "Admin", Roles.Editor => "Rédacteur", _ => "Supporter"
    };

    [HttpPost("{id}/role")]
    public async Task<IActionResult> SetRole(string id, string role)
    {
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("Compte");
        var current = await users.GetRolesAsync(user);
        var superAdmin = User.IsInRole(Roles.SuperAdmin);
        if (!Roles.All.Contains(role)) throw new BusinessRuleException("Rôle inconnu.");
        if (!superAdmin && (role is Roles.Admin or Roles.SuperAdmin || current.Any(r => r is Roles.Admin or Roles.SuperAdmin)))
            throw new BusinessRuleException("Seul un super admin peut nommer ou modifier un admin.");
        if (user.Id == users.GetUserId(User)) throw new BusinessRuleException("Vous ne pouvez pas modifier votre propre rôle.");
        if (current.Contains(Roles.SuperAdmin) && role != Roles.SuperAdmin && (await users.GetUsersInRoleAsync(Roles.SuperAdmin)).Count <= 1)
            throw new BusinessRuleException("Il doit rester au moins un super admin.");

        await users.RemoveFromRolesAsync(user, current);
        await users.AddToRoleAsync(user, role);
        await users.UpdateSecurityStampAsync(user); // la personne est reconnectée avec ses nouveaux droits
        Flash($"{user.DisplayName} : {RoleLabel(role)}.");
        return Back("/admin/utilisateurs");
    }

    [HttpPost("{id}/blocage")]
    public async Task<IActionResult> ToggleLock(string id)
    {
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("Compte");
        if (user.Id == users.GetUserId(User)) throw new BusinessRuleException("Vous ne pouvez pas bloquer votre propre compte.");
        var roles = await users.GetRolesAsync(user);
        if (!User.IsInRole(Roles.SuperAdmin) && roles.Any(r => r is Roles.Admin or Roles.SuperAdmin))
            throw new BusinessRuleException("Seul un super admin peut bloquer un admin.");
        var locked = user.LockoutEnd > DateTimeOffset.UtcNow;
        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, locked ? null : DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);
        Flash(locked ? $"{user.DisplayName} débloqué." : $"{user.DisplayName} bloqué : connexion impossible.");
        return Back("/admin/utilisateurs");
    }

    /// <summary>Mot de passe temporaire (compte oublié) : affiché une seule fois, à transmettre à la personne.</summary>
    [HttpPost("{id}/mot-de-passe")]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await users.FindByIdAsync(id) ?? throw new NotFoundException("Compte");
        var roles = await users.GetRolesAsync(user);
        if (!User.IsInRole(Roles.SuperAdmin) && roles.Any(r => r is Roles.Admin or Roles.SuperAdmin))
            throw new BusinessRuleException("Seul un super admin peut réinitialiser le mot de passe d'un admin.");
        var temp = TemporaryPassword();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, temp);
        if (!result.Succeeded) throw new BusinessRuleException(string.Join(" ", result.Errors.Select(e => e.Description)));
        await users.SetLockoutEndDateAsync(user, null);
        TempData["k-temp-password"] = $"{user.DisplayName}|{temp}";
        return Back("/admin/utilisateurs");
    }

    private static string TemporaryPassword()
    {
        const string letters = "abcdefghjkmnpqrstuvwxyz", digits = "23456789";
        static int r(int max) => System.Security.Cryptography.RandomNumberGenerator.GetInt32(max);
        var chars = Enumerable.Range(0, 6).Select(_ => letters[r(letters.Length)]).Concat(Enumerable.Range(0, 4).Select(_ => digits[r(digits.Length)]));
        return string.Concat(chars);
    }
}

/// <summary>Envoi manuel d'une notification (annonce urgente, report de match…).</summary>
[Route("admin/notifications")]
[Authorize(Roles = Roles.AdminOrAbove)]
public class NotificationsController(NotificationService notifications, Lookups lookups) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["AdminNav"] = "notifications";
        return View(new NotificationsPageVm(await notifications.AudienceAsync(ct), notifications.Enabled, await lookups.ClubsAsync(ct: ct)));
    }

    [HttpPost("")]
    public async Task<IActionResult> Send(string title, string body, string? url, int? clubId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 80 || string.IsNullOrWhiteSpace(body) || body.Length > 200)
        {
            FlashError("Titre (80 caractères max.) et message (200 max.) obligatoires.");
            return Redirect("/admin/notifications");
        }
        var count = await notifications.ManualAsync(title, body, url, clubId, ct);
        Flash(count == 0 ? "Aucun abonné ne correspond : rien n'a été envoyé." : $"Notification envoyée à {count} appareil(s).");
        return Redirect("/admin/notifications");
    }
}
