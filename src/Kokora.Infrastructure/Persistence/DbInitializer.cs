using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Content;
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

        // Catégories d'infos proposées au premier démarrage (renommables et supprimables ensuite).
        if (!await db.ArticleCategories.AnyAsync(ct) && !await db.Articles.AnyAsync(ct))
        {
            string[] names = ["Communiqués", "Résumés de matchs", "Commission", "Portraits", "Annonces"];
            db.ArticleCategories.AddRange(names.Select((n, i) => new ArticleCategory { Name = n, Slug = Slug.From(n), Order = i + 1 }));
            await db.SaveChangesAsync(ct);
        }

        var login = config["Bootstrap:SuperAdmin:Login"];
        var password = config["Bootstrap:SuperAdmin:Password"];
        if ((await users.GetUsersInRoleAsync(Roles.SuperAdmin)).Count > 0) return;
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Aucun SuperAdmin : définissez Bootstrap:SuperAdmin:Login et Bootstrap:SuperAdmin:Password " +
                              "(user-secrets ou variables d'environnement) puis redémarrez l'application.");
            return;
        }

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
