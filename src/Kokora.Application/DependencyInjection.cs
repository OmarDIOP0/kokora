using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Application.Public;
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
        services.AddScoped<ResultService>();
        services.AddScoped<QualificationService>();
        services.AddScoped<DisciplineAdminService>();
        services.AddScoped<ArticleAdminService>();
        services.AddScoped<Live.LiveMatchService>();
        services.AddScoped<PhotoAdminService>();

        services.AddSingleton<CompetitionCache>();
        services.AddScoped<StandingsService>();
        services.AddScoped<MatchQueryService>();
        services.AddScoped<StatsService>();
        services.AddScoped<DirectoryService>();
        services.AddScoped<NewsService>();

        services.AddScoped<Engagement.NotificationService>();
        services.AddScoped<Engagement.PredictionService>();
        services.AddScoped<Engagement.VoteService>();
        services.AddScoped<Engagement.CommentService>();
        services.AddScoped<Engagement.AccountDataService>();
        return services;
    }
}
