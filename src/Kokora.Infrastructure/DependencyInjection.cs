using Kokora.Application.Abstractions;
using Kokora.Infrastructure.Identity;
using Kokora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Kokora")
            ?? throw new InvalidOperationException(
                "Chaîne de connexion 'Kokora' manquante. Voir README : dotnet user-secrets set \"ConnectionStrings:Kokora\" \"...\"");

        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentity<AppUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = false; // inscription possible par téléphone
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false; // un chiffre suffit (saisie facile au téléphone)
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<FrenchIdentityErrorDescriber>();

        services.AddSingleton<IImageStore, Storage.SkiaImageStore>();
        services.AddSingleton<IPlayerFileReader, Storage.PlayerFileReader>();
        services.AddSingleton<IHtmlCleaner, Content.HtmlCleaner>();
        services.AddScoped<DbInitializer>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        // Notifications push : une seule instance sert à la fois de file d'envoi et de tâche de fond.
        services.AddSingleton<Push.WebPushService>();
        services.AddSingleton<IPushService>(sp => sp.GetRequiredService<Push.WebPushService>());
        services.AddHostedService(sp => sp.GetRequiredService<Push.WebPushService>());
        services.AddHostedService<Push.ScheduledNewsNotifier>();
        return services;
    }
}
