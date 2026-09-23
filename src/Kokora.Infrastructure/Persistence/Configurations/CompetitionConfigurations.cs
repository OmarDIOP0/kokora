using Kokora.Domain.Competitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kokora.Infrastructure.Persistence.Configurations;

public class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.HasIndex(x => x.Year).IsUnique();
    }
}

public class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.ShortName).HasMaxLength(40);
        b.Property(x => x.Slug).HasMaxLength(140);
        b.Property(x => x.Color).HasMaxLength(9);
        b.HasIndex(x => new { x.SeasonId, x.Slug }).IsUnique();
        b.HasOne(x => x.Season).WithMany(s => s.Competitions).OnDelete(DeleteBehavior.Cascade);
        b.OwnsOne(x => x.Scoring, o => o.ToJson());
        b.OwnsOne(x => x.Suspensions, o => o.ToJson());
    }
}

public class PhaseConfiguration : IEntityTypeConfiguration<Phase>
{
    public void Configure(EntityTypeBuilder<Phase> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasOne(x => x.Competition).WithMany(c => c.Phases).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> b)
    {
        b.ToTable("groups");
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasOne(x => x.Phase).WithMany(p => p.Groups).OnDelete(DeleteBehavior.Cascade);
        b.OwnsMany(x => x.Zones, o => o.ToJson());
    }
}

public class GroupTeamConfiguration : IEntityTypeConfiguration<GroupTeam>
{
    public void Configure(EntityTypeBuilder<GroupTeam> b)
    {
        b.HasKey(x => new { x.GroupId, x.ClubId });
        b.HasOne(x => x.Group).WithMany(g => g.Teams).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Club).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}

public class PointAdjustmentConfiguration : IEntityTypeConfiguration<PointAdjustment>
{
    public void Configure(EntityTypeBuilder<PointAdjustment> b)
    {
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne(x => x.Group).WithMany(g => g.PointAdjustments).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Club).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}

public class RoundConfiguration : IEntityTypeConfiguration<Round>
{
    public void Configure(EntityTypeBuilder<Round> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.HasOne(x => x.Phase).WithMany(p => p.Rounds).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QualificationConfiguration : IEntityTypeConfiguration<Qualification>
{
    public void Configure(EntityTypeBuilder<Qualification> b)
    {
        b.HasOne(x => x.SourceGroup).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.SourceMatch).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.TargetPhase).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.TargetGroup).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.TargetMatch).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Club).WithMany().OnDelete(DeleteBehavior.SetNull);
    }
}
