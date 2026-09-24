using Kokora.Application.Common;
using Kokora.Domain.Clubs;
using Kokora.Domain.Matches;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Public;

internal static class Mapping
{
    public static TeamVm? Team(Club? c) =>
        c is null ? null : new TeamVm(c.Id, c.Name, c.ShortName, c.Slug, c.PrimaryColor, c.SecondaryColor, c.LogoPath);

    public static string MatchUrl(Match m) =>
        $"/matchs/{m.Id}-{Slug.From($"{m.HomeClub?.ShortName ?? "a-designer"} {m.AwayClub?.ShortName ?? "a-designer"}", 60)}";

    /// <summary>Requête de base : match avec tout ce qu'il faut pour une ligne de match.</summary>
    public static IQueryable<Match> WithRowData(this IQueryable<Match> q) => q
        .Include(m => m.HomeClub).Include(m => m.AwayClub).Include(m => m.Stadium)
        .Include(m => m.Group).Include(m => m.Round)
        .Include(m => m.Phase).ThenInclude(p => p.Competition)
        .AsNoTracking();

    public static MatchRowVm Row(Match m, DateTimeOffset now)
    {
        var comp = m.Phase.Competition;
        return new MatchRowVm
        {
            Id = m.Id,
            Url = MatchUrl(m),
            Home = Team(m.HomeClub),
            Away = Team(m.AwayClub),
            HomePlaceholder = m.HomePlaceholder ?? "À désigner",
            AwayPlaceholder = m.AwayPlaceholder ?? "À désigner",
            KickoffAt = m.KickoffAt,
            Status = m.Status,
            HomeScore = m.HomeScore,
            AwayScore = m.AwayScore,
            HomePenalties = m.HomePenalties,
            AwayPenalties = m.AwayPenalties,
            WentToExtraTime = m.WentToExtraTime,
            Minute = m.IsLive
                ? MatchClock.Label(m.LivePeriod, m.PeriodStartedAt, comp.HalfDurationMinutes, comp.ExtraTimeHalfDurationMinutes, now)
                : null,
            Stadium = m.Stadium?.Name,
            CompetitionId = comp.Id,
            Competition = comp.Name,
            CompetitionColor = comp.Color,
            Stage = Stage(m),
            IsFeatured = m.IsFeatured,
            Period = m.LivePeriod,
            PeriodStartedAt = m.PeriodStartedAt,
            HalfMinutes = comp.HalfDurationMinutes,
            ExtraHalfMinutes = comp.ExtraTimeHalfDurationMinutes
        };
    }

    public static string Stage(Match m) =>
        m.Round is not null ? m.Round.Name
        : m.Group is not null ? (m.Matchday is { } d ? $"{m.Group.Name} · J{d}" : m.Group.Name)
        : m.Phase.Name;
}
