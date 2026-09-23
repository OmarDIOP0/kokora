using Kokora.Application.Abstractions;
using Kokora.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kokora.Infrastructure.Persistence;

/// <summary>
/// Applique les migrations, crée les rôles et, si configuré, le premier SuperAdmin
/// (Bootstrap:SuperAdmin:Login / Bootstrap:SuperAdmin:Password, en user-secrets ou variables d'environnement).
/// </summary>
public class DbInitializer(
    AppDbContext db,
    RoleManager<IdentityRole> roles,
    UserManager<AppUser> users,
    IConfiguration config,
    ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var login = config["Bootstrap:SuperAdmin:Login"];
        var password = config["Bootstrap:SuperAdmin:Password"];
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password)) return;
        if ((await users.GetUsersInRoleAsync(Roles.SuperAdmin)).Count > 0) return;

        var user = new AppUser
        {
            UserName = login,
            Email = login.Contains('@') ? login : null,
            EmailConfirmed = login.Contains('@'),
            PhoneNumber = login.Contains('@') ? null : login,
            DisplayName = config["Bootstrap:SuperAdmin:DisplayName"] ?? "Super admin"
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Création du SuperAdmin impossible : {Errors}",
                string.Join(" ; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await users.AddToRoleAsync(user, Roles.SuperAdmin);
        logger.LogInformation("SuperAdmin « {Login} » créé.", login);
    }
}
