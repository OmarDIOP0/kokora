using Kokora.Domain.Clubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kokora.Infrastructure.Persistence.Configurations;

public class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.ShortName).HasMaxLength(30);
        b.Property(x => x.Slug).HasMaxLength(140);
        b.Property(x => x.LogoPath).HasMaxLength(300);
        b.Property(x => x.PrimaryColor).HasMaxLength(9);
        b.Property(x => x.SecondaryColor).HasMaxLength(9);
        b.Property(x => x.Neighborhood).HasMaxLength(120);
        b.Property(x => x.Zone).HasMaxLength(20);
        b.Property(x => x.ManagerName).HasMaxLength(120);
        b.Property(x => x.ContactPhone).HasMaxLength(30);
        b.HasIndex(x => x.Slug).IsUnique();
        b.Ignore(x => x.Initials);
    }
}

public class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> b)
    {
        b.Property(x => x.FirstName).HasMaxLength(80);
        b.Property(x => x.LastName).HasMaxLength(80);
        b.Property(x => x.Nickname).HasMaxLength(60);
        b.Property(x => x.Slug).HasMaxLength(180);
        b.Property(x => x.PhotoPath).HasMaxLength(300);
        b.HasIndex(x => x.Slug).IsUnique();
        b.HasIndex(x => new { x.LastName, x.FirstName });
        b.Ignore(x => x.FullName);
        b.Ignore(x => x.DisplayName);
    }
}

public class SquadMemberConfiguration : IEntityTypeConfiguration<SquadMember>
{
    public void Configure(EntityTypeBuilder<SquadMember> b)
    {
        b.Property(x => x.LicenseNumber).HasMaxLength(40);
        b.HasIndex(x => new { x.SeasonId, x.PlayerId }).IsUnique();
        b.HasOne(x => x.Season).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Club).WithMany(c => c.Squad).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Player).WithMany(p => p.Memberships).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StadiumConfiguration : IEntityTypeConfiguration<Stadium>
{
    public void Configure(EntityTypeBuilder<Stadium> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Neighborhood).HasMaxLength(120);
    }
}

public class RefereeConfiguration : IEntityTypeConfiguration<Referee>
{
    public void Configure(EntityTypeBuilder<Referee> b)
    {
        b.Property(x => x.FullName).HasMaxLength(120);
        b.Property(x => x.Phone).HasMaxLength(30);
    }
}
