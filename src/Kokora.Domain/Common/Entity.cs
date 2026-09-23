namespace Kokora.Domain.Common;

public abstract class Entity
{
    public int Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Données de démonstration fictives, supprimables en masse depuis l'admin.</summary>
public interface IDemoData
{
    bool IsDemo { get; set; }
}

/// <summary>Entité dont les modifications sont tracées dans le journal d'audit.</summary>
public interface IAuditable;
