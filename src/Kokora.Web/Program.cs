using System.Globalization;
using Kokora.Application.Abstractions;
using Kokora.Infrastructure;
using Kokora.Infrastructure.Persistence;
using Kokora.Web.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

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
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erreur");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/erreur", "?code={0}");
app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}")
   .WithStaticAssets();

app.Run();
