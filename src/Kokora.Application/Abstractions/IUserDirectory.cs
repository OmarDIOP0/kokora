namespace Kokora.Application.Abstractions;

/// <summary>Lecture des comptes (implémentée avec Identity dans l'infrastructure).</summary>
public interface IUserDirectory
{
    /// <summary>Nom affiché de chaque compte (classements des pronostics, commentaires).</summary>
    Task<Dictionary<string, string>> DisplayNamesAsync(IEnumerable<string> userIds, CancellationToken ct = default);

    Task<string?> DisplayNameAsync(string userId, CancellationToken ct = default);
}
