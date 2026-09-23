using System.Text.Json;
using Kokora.Application.Abstractions;
using Kokora.Domain.Common;
using Kokora.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kokora.Infrastructure.Persistence;

/// <summary>
/// Met à jour les horodatages et écrit une ligne d'audit (valeurs avant/après)
/// pour chaque entité <see cref="IAuditable"/> modifiée. Enregistré en scoped (un par DbContext).
/// </summary>
public class AuditInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    private List<(EntityEntry Entry, AuditLog Log)> _addedPending = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context) Track(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context) Track(context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (FixAddedIds() && eventData.Context is { } context)
            await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (FixAddedIds() && eventData.Context is { } context)
            context.SaveChanges();
        return result;
    }

    private void Track(DbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        _addedPending = [];

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is Entity entity && entry.State == EntityState.Modified)
                entity.UpdatedAt = now;

            if (entry.Entity is not IAuditable) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var changes = new Dictionary<string, object?>();
            foreach (var p in entry.Properties)
            {
                var name = p.Metadata.Name;
                if (name is nameof(Entity.UpdatedAt) or nameof(Entity.CreatedAt)) continue;
                switch (entry.State)
                {
                    case EntityState.Added when p.CurrentValue is not null && !p.IsTemporary:
                        changes[name] = p.CurrentValue;
                        break;
                    case EntityState.Deleted:
                        changes[name] = p.OriginalValue;
                        break;
                    case EntityState.Modified when p.IsModified && !Equals(p.OriginalValue, p.CurrentValue):
                        changes[name] = new { avant = p.OriginalValue, apres = p.CurrentValue };
                        break;
                }
            }
            if (entry.State == EntityState.Modified && changes.Count == 0) continue;

            var log = new AuditLog
            {
                At = now,
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                IpAddress = currentUser.IpAddress,
                Action = entry.State.ToString(),
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = PrimaryKey(entry),
                Changes = JsonSerializer.Serialize(changes)
            };
            context.Set<AuditLog>().Add(log);
            if (entry.State == EntityState.Added) _addedPending.Add((entry, log));
        }
    }

    /// <summary>Renseigne les clés générées par la base après insertion. Renvoie vrai s'il faut resauvegarder.</summary>
    private bool FixAddedIds()
    {
        if (_addedPending.Count == 0) return false;
        var pending = _addedPending;
        _addedPending = [];
        foreach (var (entry, log) in pending) log.EntityId = PrimaryKey(entry);
        return true;
    }

    private static string? PrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;
        return string.Join("|", key.Properties.Select(p => entry.Property(p.Name).CurrentValue));
    }
}
