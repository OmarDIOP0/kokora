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
        var connectionString = ToNpgsql(config.GetConnectionString("Kokora")
            ?? throw new InvalidOperationException(
                "Chaîne de connexion 'Kokora' manquante. Voir README : dotnet user-secrets set \"ConnectionStrings:Kokora\" \"...\""));

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

        // Fichiers téléversés : disque (par défaut) ou base de données (Storage:Mode = Database, ex. Render gratuit).
        if (IsDatabaseStorage(config))
            services.AddSingleton<Storage.IBlobStore, Storage.DatabaseBlobStore>();
        else
            services.AddSingleton<Storage.IBlobStore>(sp => new Storage.LocalBlobStore(config["Storage:UploadsPath"] is { Length: > 0 } custom
                ? custom
                : Path.Combine(sp.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().ContentRootPath, "wwwroot", "uploads")));
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

        services.AddSingleton<Analytics.VisitRecorder>();
        services.AddSingleton<IVisitCounter>(sp => sp.GetRequiredService<Analytics.VisitRecorder>());
        services.AddHostedService(sp => sp.GetRequiredService<Analytics.VisitRecorder>());
        return services;
    }

    public static bool IsDatabaseStorage(IConfiguration config) =>
        string.Equals(config["Storage:Mode"], "Database", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Accepte aussi le format URL fourni par les hébergeurs (Render, Neon, Heroku) :
    /// postgresql://utilisateur:motdepasse@hôte:port/base → format Npgsql.
    /// </summary>
    public static string ToNpgsql(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return connectionString;
        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            // Hôte distant : chiffrement exigé (Render, Neon) ; en local, laissé au choix du serveur.
            SslMode = uri.Host is "localhost" or "127.0.0.1" || !uri.Host.Contains('.') ? Npgsql.SslMode.Prefer : Npgsql.SslMode.Require
        };
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) && kv.Length == 2
                && Enum.TryParse<Npgsql.SslMode>(kv[1], true, out var mode))
                builder.SslMode = mode;
        }
        return builder.ConnectionString;
    }
}
