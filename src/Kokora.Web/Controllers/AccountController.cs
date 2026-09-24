using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Kokora.Application.Abstractions;
using Kokora.Application.Engagement;
using Kokora.Application.Public;
using Kokora.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
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

public class RegisterInput
{
    [Required(ErrorMessage = "Indiquez le nom affiché (prénom, surnom…).")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "Entre 2 et 40 caractères.")]
    [Display(Name = "Nom affiché")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Indiquez votre e-mail ou votre numéro de téléphone.")]
    [StringLength(120)]
    [Display(Name = "E-mail ou téléphone")]
    public string Login { get; set; } = "";

    [Required(ErrorMessage = "Choisissez un mot de passe.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "8 caractères minimum.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mot de passe")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Confirmez le mot de passe.")]
    [Compare(nameof(Password), ErrorMessage = "Les deux mots de passe ne correspondent pas.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmation")]
    public string ConfirmPassword { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class ProfileInput
{
    [Required(ErrorMessage = "Indiquez le nom affiché.")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "Entre 2 et 40 caractères.")]
    [Display(Name = "Nom affiché")]
    public string DisplayName { get; set; } = "";
}

public class PasswordInput
{
    [Required(ErrorMessage = "Indiquez votre mot de passe actuel.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mot de passe actuel")]
    public string Current { get; set; } = "";

    [Required(ErrorMessage = "Choisissez un nouveau mot de passe.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "8 caractères minimum.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nouveau mot de passe")]
    public string New { get; set; } = "";

    [Compare(nameof(New), ErrorMessage = "Les deux mots de passe ne correspondent pas.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmation")]
    public string Confirm { get; set; } = "";
}

public class AccountPageVm
{
    public required AppUser User { get; init; }
    public bool IsStaff { get; init; }
    public ProfileInput Profile { get; init; } = new();
    public PasswordInput Password { get; init; } = new();
    public IReadOnlyList<TeamVm> Favorites { get; init; } = [];
    public IReadOnlyList<(MatchRowVm Match, PredictionVm Prediction)> Predictions { get; init; } = [];
    public LeaderRow? Rank { get; init; }
    public string? Tab { get; init; }
}

public record FavoritesRequest(List<int> Ids, bool Merge);

[Route("compte")]
public class AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> users, AccountDataService data,
    PredictionService predictions, MatchQueryService matches, IAppDbContext db) : Controller
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true) return Redirect("/compte/connexion?returnUrl=%2Fcompte");
        var user = await users.GetUserAsync(User);
        if (user is null) { await signIn.SignOutAsync(); return Redirect("/compte/connexion"); }
        return View(await PageAsync(user, null, ct));
    }

    private async Task<AccountPageVm> PageAsync(AppUser user, string? tab, CancellationToken ct, ProfileInput? profile = null, PasswordInput? password = null)
    {
        ViewData["Nav"] = "plus";
        var favIds = await data.FavoritesAsync(user.Id, ct);
        var favorites = await db.Clubs.AsNoTracking().Where(c => favIds.Contains(c.Id)).OrderBy(c => c.Name)
            .Select(c => new TeamVm(c.Id, c.Name, c.ShortName, c.Slug, c.PrimaryColor, c.SecondaryColor, c.LogoPath)).ToListAsync(ct);
        var season = await matches.CurrentSeasonAsync(ct);
        return new AccountPageVm
        {
            User = user, Tab = tab,
            IsStaff = User.IsInRole(Roles.SuperAdmin) || User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Editor),
            Profile = profile ?? new ProfileInput { DisplayName = user.DisplayName },
            Password = password ?? new PasswordInput(),
            Favorites = favorites,
            Predictions = await predictions.HistoryAsync(user.Id, 8, ct),
            Rank = season is null ? null : (await predictions.LeaderboardAsync(season.Id, user.Id, 0, ct)).Me
        };
    }

    // ------------------------------------------------------------ Connexion

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

    // ------------------------------------------------------------ Inscription

    [HttpGet("inscription")]
    public IActionResult Register(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturn(returnUrl));
        ViewData["Nav"] = "plus";
        return View(new RegisterInput { ReturnUrl = returnUrl });
    }

    [HttpPost("inscription")]
    [EnableRateLimiting("register")]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        ViewData["Nav"] = "plus";
        var id = LoginId.Parse(input.Login);
        if (id is null) ModelState.AddModelError(nameof(input.Login), "E-mail ou numéro de téléphone sénégalais invalide (ex. 77 123 45 67).");
        if (!ModelState.IsValid) return View(input);

        if (await FindAsync(id!.UserName) is not null)
        {
            ModelState.AddModelError(nameof(input.Login), "Un compte existe déjà avec cet identifiant. Connectez-vous.");
            return View(input);
        }
        var user = new AppUser
        {
            UserName = id.UserName, Email = id.Email, PhoneNumber = id.Phone, DisplayName = input.DisplayName.Trim(),
            LastSeenAt = DateTimeOffset.UtcNow
        };
        var result = await users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(e.Code.Contains("Password") ? nameof(input.Password) : "", e.Description);
            return View(input);
        }
        await users.AddToRoleAsync(user, Roles.User);
        await signIn.SignInAsync(user, isPersistent: true);
        TempData["k-welcome"] = "1";
        return LocalRedirect(SafeReturn(input.ReturnUrl));
    }

    // ------------------------------------------------------------ Profil

    [Authorize, HttpPost("profil")]
    public async Task<IActionResult> Profile([Bind(Prefix = "Profile")] ProfileInput input, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException();
        if (!ModelState.IsValid) return View("Index", await PageAsync(user, "profil", ct, profile: input));
        user.DisplayName = input.DisplayName.Trim();
        await users.UpdateAsync(user);
        TempData["k-account"] = "Profil enregistré.";
        return Redirect("/compte");
    }

    [Authorize, HttpPost("mot-de-passe")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] PasswordInput input, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException();
        if (ModelState.IsValid)
        {
            var result = await users.ChangePasswordAsync(user, input.Current, input.New);
            if (result.Succeeded)
            {
                await signIn.RefreshSignInAsync(user);
                TempData["k-account"] = "Mot de passe modifié.";
                return Redirect("/compte");
            }
            foreach (var e in result.Errors)
                ModelState.AddModelError(e.Code == "PasswordMismatch" ? "Password.Current" : "Password.New", e.Description);
        }
        return View("Index", await PageAsync(user, "securite", ct, password: new PasswordInput()));
    }

    /// <summary>Suppression définitive du compte et de ses données (pronostics, votes, commentaires, favoris).</summary>
    [Authorize, HttpPost("supprimer")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Delete(string password, CancellationToken ct)
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException();
        if (User.IsInRole(Roles.SuperAdmin))
        {
            TempData["k-account"] = "Un SuperAdmin ne peut pas supprimer son compte ici.";
            return Redirect("/compte");
        }
        if (!await users.CheckPasswordAsync(user, password ?? ""))
        {
            TempData["k-account"] = "Mot de passe incorrect : compte non supprimé.";
            return Redirect("/compte");
        }
        await data.DeleteAllAsync(user.Id, ct);
        await users.DeleteAsync(user);
        await signIn.SignOutAsync();
        return Redirect("/?compte=supprime");
    }

    // ------------------------------------------------------------ Équipes suivies (synchronisation)

    [Authorize, HttpGet("favoris")]
    public async Task<IActionResult> Favorites(CancellationToken ct) => Json(await data.FavoritesAsync(UserId!, ct));

    [Authorize, HttpPost("favoris")]
    public async Task<IActionResult> SaveFavorites([FromBody] FavoritesRequest request, CancellationToken ct) =>
        Json(await data.SaveFavoritesAsync(UserId!, request.Ids ?? [], request.Merge, ct));

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

/// <summary>Identifiant de connexion : e-mail, ou numéro sénégalais normalisé au format +221XXXXXXXXX.</summary>
public record LoginId(string UserName, string? Email, string? Phone)
{
    public static LoginId? Parse(string? login)
    {
        login = (login ?? "").Trim();
        if (login.Contains('@'))
            return new EmailAddressAttribute().IsValid(login) ? new LoginId(login.ToLowerInvariant(), login.ToLowerInvariant(), null) : null;
        var digits = new string(login.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00221")) digits = digits[5..];
        else if (digits.StartsWith("221") && digits.Length == 12) digits = digits[3..];
        // Mobiles (7x) et fixes (33) : 9 chiffres.
        if (digits.Length != 9 || !(digits.StartsWith('7') || digits.StartsWith('3'))) return null;
        var phone = "+221" + digits;
        return new LoginId(phone, null, phone);
    }
}
