using Kokora.Domain.Discipline;
using Kokora.Domain.Matches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kokora.Infrastructure.Persistence.Configurations;

public class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> b)
    {
        b.Property(x => x.HomePlaceholder).HasMaxLength(80);
        b.Property(x => x.AwayPlaceholder).HasMaxLength(80);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Phase).WithMany(p => p.Matches).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Group).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Round).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.HomeClub).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AwayClub).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Stadium).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Referee).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.FirstLeg).WithMany().HasForeignKey(x => x.FirstLegMatchId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => x.KickoffAt);
        b.HasIndex(x => x.Status);
        b.Ignore(x => x.HasResult);
        b.Ignore(x => x.IsLive);
    }
}

public class MatchEventConfiguration : IEntityTypeConfiguration<MatchEvent>
{
    public void Configure(EntityTypeBuilder<MatchEvent> b)
    {
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasOne(x => x.Match).WithMany(m => m.Events).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Club).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AssistPlayer).WithMany().HasForeignKey(x => x.AssistPlayerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PlayerOut).WithMany().HasForeignKey(x => x.PlayerOutId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.MatchId, x.IsCancelled });
        b.HasIndex(x => new { x.PlayerId, x.Type });
        b.Property(x => x.ClientKey).HasMaxLength(40);
        // Une action du mode terrain renvoyée deux fois (réseau instable) n'est enregistrée qu'une fois.
        b.HasIndex(x => new { x.MatchId, x.ClientKey }).IsUnique().HasFilter("client_key IS NOT NULL");
        b.Ignore(x => x.MinuteLabel);
    }
}

public class LineupEntryConfiguration : IEntityTypeConfiguration<LineupEntry>
{
    public void Configure(EntityTypeBuilder<LineupEntry> b)
    {
        b.HasOne(x => x.Match).WithMany(m => m.Lineups).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Player).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.MatchId, x.PlayerId }).IsUnique();
    }
}

public class SuspensionConfiguration : IEntityTypeConfiguration<Suspension>
{
    public void Configure(EntityTypeBuilder<Suspension> b)
    {
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne(x => x.Player).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Competition).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Club).WithMany().OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.TriggerMatch).WithMany().OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProtestConfiguration : IEntityTypeConfiguration<Protest>
{
    public void Configure(EntityTypeBuilder<Protest> b)
    {
        b.Property(x => x.Subject).HasMaxLength(500);
        b.Property(x => x.Decision).HasMaxLength(2000);
        b.HasOne(x => x.Match).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Club).WithMany().OnDelete(DeleteBehavior.Restrict);
    }
}
