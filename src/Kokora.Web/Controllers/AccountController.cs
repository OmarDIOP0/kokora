using System.ComponentModel.DataAnnotations;
using Kokora.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Controllers;

public class LoginInput
{
    [Required(ErrorMessage = "Indiquez votre e-mail ou votre numéro de téléphone.")]
    [Display(Name = "E-mail ou téléphone")]
    public string Login { get; set; } = "";

    [Required(ErrorMessage = "Indiquez votre mot de passe.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mot de passe")]
    public string Password { get; set; } = "";

    [Display(Name = "Rester connecté")]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }
}

[Route("compte")]
public class AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> users) : Controller
{
    /// <summary>Espace compte (profil complet en phase 7) : l'équipe d'organisation est dirigée vers l'admin.</summary>
    [HttpGet("")]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true) return Redirect("/compte/connexion");
        return Redirect(Kokora.Application.Abstractions.Roles.All.Take(3).Any(User.IsInRole) ? "/admin" : "/");
    }

    [HttpGet("connexion")]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturn(returnUrl));
        ViewData["Nav"] = "plus";
        return View(new LoginInput { ReturnUrl = returnUrl });
    }

    [HttpPost("connexion")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        ViewData["Nav"] = "plus";
        if (!ModelState.IsValid) return View(input);

        var user = await FindAsync(input.Login);
        if (user is null)
        {
            ModelState.AddModelError("", "Identifiant ou mot de passe incorrect.");
            return View(input);
        }

        var result = await signIn.PasswordSignInAsync(user, input.Password, input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            user.LastSeenAt = DateTimeOffset.UtcNow;
            await users.UpdateAsync(user);
            return LocalRedirect(SafeReturn(input.ReturnUrl));
        }
        ModelState.AddModelError("", result.IsLockedOut
            ? "Trop de tentatives. Réessayez dans 15 minutes."
            : "Identifiant ou mot de passe incorrect.");
        return View(input);
    }

    [HttpPost("deconnexion")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return LocalRedirect("/");
    }

    [HttpGet("acces-refuse")]
    public IActionResult AccessDenied()
    {
        ViewData["Nav"] = "plus";
        Response.StatusCode = 403;
        return View();
    }

    /// <summary>Connexion par e-mail, numéro de téléphone (formats locaux acceptés) ou identifiant.</summary>
    private async Task<AppUser?> FindAsync(string login)
    {
        login = login.Trim();
        if (login.Contains('@')) return await users.FindByEmailAsync(login) ?? await users.FindByNameAsync(login);
        var digits = new string(login.Where(char.IsDigit).ToArray());
        if (digits.Length >= 9)
        {
            var local = digits.Length > 9 && digits.StartsWith("221") ? digits[3..] : digits;
            var byPhone = await users.Users.FirstOrDefaultAsync(u => u.PhoneNumber != null &&
                (u.PhoneNumber == login || u.PhoneNumber.EndsWith(local)));
            if (byPhone is not null) return byPhone;
        }
        return await users.FindByNameAsync(login);
    }

    private string SafeReturn(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
}
