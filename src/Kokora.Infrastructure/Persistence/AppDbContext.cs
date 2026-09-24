using Kokora.Application.Abstractions;
using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Content;
using Kokora.Domain.Discipline;
using Kokora.Domain.Matches;
using Kokora.Domain.Users;
using Kokora.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options), IAppDbContext, Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.IDataProtectionKeyContext
{
    /// <summary>Fichiers téléversés quand Storage:Mode = Database (hébergement sans disque permanent).</summary>
    public DbSet<Storage.StoredFile> StoredFiles => Set<Storage.StoredFile>();
    /// <summary>Clés de chiffrement des cookies quand elles sont conservées en base.</summary>
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys =>
        Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<Phase> Phases => Set<Phase>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupTeam> GroupTeams => Set<GroupTeam>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<PointAdjustment> PointAdjustments => Set<PointAdjustment>();

    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<SquadMember> SquadMembers => Set<SquadMember>();
    public DbSet<Stadium> Stadiums => Set<Stadium>();
    public DbSet<Referee> Referees => Set<Referee>();

    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();
    public DbSet<LineupEntry> LineupEntries => Set<LineupEntry>();
    public DbSet<Suspension> Suspensions => Set<Suspension>();
    public DbSet<Protest> Protests => Set<Protest>();

    public DbSet<Article> Articles => Set<Article>();
    public DbSet<ArticleCategory> ArticleCategories => Set<ArticleCategory>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<FavoriteClub> FavoriteClubs => Set<FavoriteClub>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<ManOfTheMatchVote> ManOfTheMatchVotes => Set<ManOfTheMatchVote>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DailyVisit> DailyVisits => Set<DailyVisit>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Storage.StoredFile>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(300);
            e.Property(x => x.ContentType).HasMaxLength(100);
        });

        // Identity impose ses noms de tables (AspNetUsers…) : on les aligne sur la convention snake_case.
        b.Entity<AppUser>().ToTable("users");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityRole>().ToTable("roles");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().ToTable("user_roles");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<string>>().ToTable("user_claims");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().ToTable("user_logins");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().ToTable("user_tokens");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>>().ToTable("role_claims");

        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
