using System.Security.Claims;
using System.Text.Encodings.Web;
using Kokora.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Kokora.Web.Tests;

/// <summary>
/// Application complète sur une base PostgreSQL dédiée aux tests (kokora_tests, recréée à chaque exécution).
/// La chaîne de connexion vient de KOKORA_TEST_CONNECTION ou des user-secrets du projet Web (base remplacée).
/// Authentification simulée : en-tête « X-Test-Role » (absent = visiteur anonyme).
/// </summary>
public class KokoraWebFactory : WebApplicationFactory<Program>
{
    public const string RoleHeader = "X-Test-Role";
    private readonly string _connectionString;

    public KokoraWebFactory() : this("kokora_tests") { }

    public KokoraWebFactory(string database)
    {
        var baseCs = Environment.GetEnvironmentVariable("KOKORA_TEST_CONNECTION")
            ?? new ConfigurationBuilder().AddUserSecrets<Program>().Build().GetConnectionString("Kokora")
            ?? throw new InvalidOperationException(
                "Aucune base de test : définissez KOKORA_TEST_CONNECTION ou le user-secret ConnectionStrings:Kokora du projet Web.");

        var builder = new NpgsqlConnectionStringBuilder(baseCs) { Database = database };
        _connectionString = builder.ConnectionString;
        RecreateDatabase(builder);
    }

    private static void RecreateDatabase(NpgsqlConnectionStringBuilder target)
    {
        var admin = new NpgsqlConnectionStringBuilder(target.ConnectionString) { Database = "postgres", Pooling = false };
        using var conn = new NpgsqlConnection(admin.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE); CREATE DATABASE \"{target.Database}\";";
        cmd.ExecuteNonQuery();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Appliqué avant l'exécution de Program : remplace la base configurée en user-secrets.
        builder.UseSetting("ConnectionStrings:Kokora", _connectionString);
        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                    o.DefaultForbidScheme = TestAuthHandler.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });
    }

    public HttpClient ClientAs(string? role)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add(RoleHeader, role);
        return client;
    }
}

public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(KokoraWebFactory.RoleHeader, out var role) || string.IsNullOrEmpty(role))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "testeur@kokora.test"),
            new Claim(ClaimTypes.Role, role!)
        ], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 302;
        Response.Headers.Location = "/compte/connexion";
        return Task.CompletedTask;
    }
}

public static class RolesForTests
{
    public const string Admin = Roles.Admin;
    public const string Editor = Roles.Editor;
}
