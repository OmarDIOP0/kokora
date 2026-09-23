using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kokora.Infrastructure.Persistence;

/// <summary>Utilisé uniquement par « dotnet ef » (création des migrations).</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("KOKORA_DESIGN_CONNECTION")
                 ?? "Host=localhost;Database=kokora;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options);
    }

}
