using Kokora.Application.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace Kokora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SeasonAdminService>();
        services.AddScoped<CompetitionAdminService>();
        services.AddScoped<ClubAdminService>();
        services.AddScoped<VenueAdminService>();
        services.AddScoped<PlayerAdminService>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<DemoDataService>();
        services.AddScoped<DashboardService>();
        return services;
    }
}
