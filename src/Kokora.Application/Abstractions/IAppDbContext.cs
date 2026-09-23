using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Content;
using Kokora.Domain.Discipline;
using Kokora.Domain.Matches;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Season> Seasons { get; }
    DbSet<Competition> Competitions { get; }
    DbSet<Phase> Phases { get; }
    DbSet<Group> Groups { get; }
    DbSet<GroupTeam> GroupTeams { get; }
    DbSet<Round> Rounds { get; }
    DbSet<Qualification> Qualifications { get; }
    DbSet<PointAdjustment> PointAdjustments { get; }

    DbSet<Club> Clubs { get; }
    DbSet<Player> Players { get; }
    DbSet<SquadMember> SquadMembers { get; }
    DbSet<Stadium> Stadiums { get; }
    DbSet<Referee> Referees { get; }

    DbSet<Match> Matches { get; }
    DbSet<MatchEvent> MatchEvents { get; }
    DbSet<LineupEntry> LineupEntries { get; }
    DbSet<Suspension> Suspensions { get; }
    DbSet<Protest> Protests { get; }

    DbSet<Article> Articles { get; }
    DbSet<ArticleCategory> ArticleCategories { get; }
    DbSet<Tag> Tags { get; }
    DbSet<Photo> Photos { get; }
    DbSet<Comment> Comments { get; }

    DbSet<FavoriteClub> FavoriteClubs { get; }
    DbSet<PushSubscription> PushSubscriptions { get; }
    DbSet<Prediction> Predictions { get; }
    DbSet<ManOfTheMatchVote> ManOfTheMatchVotes { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<DailyVisit> DailyVisits { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
