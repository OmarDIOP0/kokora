using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Kokora.Application.Common;

/// <summary>
/// Cache mémoire des classements, tableaux et statistiques, par compétition.
/// Chaque saisie de résultat « change la version » de la compétition : toutes ses entrées deviennent obsolètes d'un coup.
/// </summary>
public class CompetitionCache(IMemoryCache cache)
{
    /// <summary>Clé des données qui couvrent toute la saison : invalidée à chaque changement de n'importe quelle compétition.</summary>
    public const int SeasonWide = 0;
    private static readonly ConcurrentDictionary<int, int> Versions = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async Task<T> GetOrCreateAsync<T>(int competitionId, string key, Func<Task<T>> factory)
    {
        var fullKey = $"c{competitionId}:v{Versions.GetOrAdd(competitionId, 0)}:{key}";
        if (cache.TryGetValue(fullKey, out T? value) && value is not null) return value;
        value = await factory();
        cache.Set(fullKey, value, Lifetime);
        return value;
    }

    public void Invalidate(int competitionId)
    {
        Versions.AddOrUpdate(competitionId, 1, (_, v) => v + 1);
        Versions.AddOrUpdate(SeasonWide, 1, (_, v) => v + 1);
    }
}
