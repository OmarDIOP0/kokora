using System.Globalization;
using System.Threading.RateLimiting;
using Kokora.Application;
using Kokora.Application.Abstractions;
using Kokora.Infrastructure;
using Kokora.Infrastructure.Persistence;
using Kokora.Web.Areas.Admin;
using Kokora.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

// Données persistantes hors base : clés de chiffrement des cookies, clés VAPID (à sauvegarder avec la base).
var dataPath = builder.Configuration["Storage:DataPath"] is { Length: > 0 } customData
    ? customData
    : Path.Combine(builder.Environment.ContentRootPath, "App_Data");
builder.Configuration["Storage:DataPath"] = dataPath;
// Sans clés persistantes, chaque redémarrage (ou conteneur recréé) déconnecterait tout le monde.
builder.Services.AddDataProtection()
    .SetApplicationName("Kokora")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddScoped<AdminSeason>();
builder.Services.AddScoped<Kokora.Web.Areas.Admin.Models.Lookups>();
builder.Services.AddScoped<PublicContext>();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("base-de-donnees");
builder.Services.AddSingleton<ILiveNotifier, Kokora.Web.Live.SignalRLiveNotifier>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    FrenchModelBinding.Configure(options.ModelBindingMessageProvider);
});
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
// Les accents (é, è, à…) sont écrits tels quels dans le HTML plutôt qu'en entités : pages plus légères et lisibles.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 12 * 1024 * 1024);

// Limitation des tentatives de connexion par adresse IP (en plus du verrouillage de compte d'Identity).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
    // Création de comptes : 5 par heure et par adresse IP (limite les inscriptions en masse).
    o.AddPolicy("register", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1) }));
});

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/compte/connexion";
    o.LogoutPath = "/compte/deconnexion";
    o.AccessDeniedPath = "/compte/acces-refuse";
    o.Cookie.Name = "kokora.auth";
    o.ExpireTimeSpan = TimeSpan.FromDays(60);
    o.SlidingExpiration = true;
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

var fr = new CultureInfo("fr-FR");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(fr),
    SupportedCultures = [fr],
    SupportedUICultures = [fr]
});

// Migrations + rôles + premier SuperAdmin.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync();
    }
    catch (Exception ex) when (app.Environment.IsDevelopment())
    {
        // En développement, l'application démarre quand même (ex. page /design) si PostgreSQL n'est pas prêt.
        app.Logger.LogError(ex, "Base de données indisponible : vérifiez ConnectionStrings:Kokora (voir README).");
    }
}

app.UseForwardedHeaders();
app.UseSecurityHeaders(app.Environment.IsDevelopment());
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erreur");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/erreur", "?code={0}");
app.UseResponseCompression();
app.UseHttpsRedirection();
// Fichiers statiques : les ressources versionnées (?v=…) et les images téléversées (noms uniques) sont gardées
// longtemps par le navigateur ; le service worker et le manifeste sont toujours revalidés.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        var headers = ctx.Context.Response.Headers;
        if (path is "/sw.js" or "/manifest.webmanifest")
            headers.CacheControl = "no-cache";
        else if (path.StartsWith("/uploads/") || (ctx.Context.Request.Query.ContainsKey("v")
                 && (path.StartsWith("/dist/") || path.StartsWith("/fonts/") || path.StartsWith("/icons/"))))
            headers.CacheControl = "public, max-age=31536000, immutable";
        else if (path.StartsWith("/fonts/") || path.StartsWith("/icons/"))
            headers.CacheControl = "public, max-age=604800";
    }
});
// Téléversements rangés hors de wwwroot (Storage:UploadsPath, ex. volume Docker) : servis sous /uploads.
if (app.Configuration["Storage:UploadsPath"] is { Length: > 0 } uploadsPath)
{
    Directory.CreateDirectory(uploadsPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
        RequestPath = "/uploads",
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable"
    });
}
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseVisitCounting();

app.MapStaticAssets();
app.MapControllers();
app.MapHealthChecks("/sante"); // supervision (disponibilité de l'application et de la base)
app.MapHub<Kokora.Web.Live.LiveHub>(Kokora.Web.Live.LiveHub.Path);
app.MapControllerRoute(name: "admin", pattern: "admin/{controller=Dashboard}/{action=Index}/{id?}", defaults: new { area = "Admin" })
   .WithStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}")
   .WithStaticAssets();

app.Run();

public partial class Program;
