using Kokora.Domain.Content;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kokora.Infrastructure.Persistence.Configurations;

public class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Slug).HasMaxLength(220);
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.CoverImagePath).HasMaxLength(300);
        b.Property(x => x.AuthorName).HasMaxLength(120);
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.Status, x.PublishedAt });
        b.HasOne(x => x.Category).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Tags).WithMany(t => t.Articles).UsingEntity(j => j.ToTable("article_tags"));
        b.HasMany(x => x.Clubs).WithMany().UsingEntity(j => j.ToTable("article_clubs"));
        b.HasMany(x => x.Matches).WithMany().UsingEntity(j => j.ToTable("article_matches"));
    }
}

public class ArticleCategoryConfiguration : IEntityTypeConfiguration<ArticleCategory>
{
    public void Configure(EntityTypeBuilder<ArticleCategory> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.Slug).HasMaxLength(100);
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.Property(x => x.Name).HasMaxLength(60);
        b.Property(x => x.Slug).HasMaxLength(80);
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

public class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> b)
    {
        b.Property(x => x.Path).HasMaxLength(300);
        b.Property(x => x.ThumbnailPath).HasMaxLength(300);
        b.Property(x => x.Caption).HasMaxLength(300);
        b.Property(x => x.Credit).HasMaxLength(120);
        b.HasOne(x => x.Match).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Article).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> b)
    {
        b.Property(x => x.Body).HasMaxLength(2000);
        b.Property(x => x.UserDisplayName).HasMaxLength(120);
        b.HasOne(x => x.Article).WithMany(a => a.Comments).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ArticleId, x.Status });
    }
}

public class FavoriteClubConfiguration : IEntityTypeConfiguration<FavoriteClub>
{
    public void Configure(EntityTypeBuilder<FavoriteClub> b)
    {
        b.HasKey(x => new { x.UserId, x.ClubId });
        b.HasOne(x => x.Club).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> b)
    {
        b.Property(x => x.Endpoint).HasMaxLength(1000);
        b.Property(x => x.P256dh).HasMaxLength(200);
        b.Property(x => x.Auth).HasMaxLength(100);
        b.Property(x => x.UserAgent).HasMaxLength(300);
        b.HasIndex(x => x.Endpoint).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}

public class PredictionConfiguration : IEntityTypeConfiguration<Prediction>
{
    public void Configure(EntityTypeBuilder<Prediction> b)
    {
        b.HasIndex(x => new { x.UserId, x.MatchId }).IsUnique();
        b.HasOne(x => x.Match).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class ManOfTheMatchVoteConfiguration : IEntityTypeConfiguration<ManOfTheMatchVote>
{
    public void Configure(EntityTypeBuilder<ManOfTheMatchVote> b)
    {
        b.HasIndex(x => new { x.UserId, x.MatchId }).IsUnique();
        b.HasOne(x => x.Match).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Player).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.Action).HasMaxLength(60);
        b.Property(x => x.EntityType).HasMaxLength(80);
        b.Property(x => x.EntityId).HasMaxLength(60);
        b.Property(x => x.UserName).HasMaxLength(256);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.Changes).HasColumnType("jsonb");
        b.HasIndex(x => x.At);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

public class DailyVisitConfiguration : IEntityTypeConfiguration<DailyVisit>
{
    public void Configure(EntityTypeBuilder<DailyVisit> b)
    {
        b.HasKey(x => new { x.Day, x.Section });
        b.Property(x => x.Section).HasMaxLength(40);
    }
}
