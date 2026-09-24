using System.Collections.Concurrent;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kokora.Infrastructure.Analytics;

/// <summary>
/// Compteur de pages vues : additionné en mémoire puis écrit en base chaque minute (une ligne par jour et par rubrique).
/// Aucun cookie, aucune adresse IP, aucun identifiant : seulement des totaux.
/// </summary>
public class VisitRecorder(IServiceScopeFactory scopes, ILogger<VisitRecorder> logger) : BackgroundService, IVisitCounter
{
    private readonly ConcurrentDictionary<(DateOnly Day, string Section), int> _pending = new();

    public void Count(string section) =>
        _pending.AddOrUpdate((KokoraTime.Today, section), 1, (_, n) => n + 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await FlushAsync(CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await FlushAsync(CancellationToken.None); // arrêt de l'application : on n'oublie pas la dernière minute
        }
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        if (_pending.IsEmpty) return;
        var batch = new List<(DateOnly Day, string Section, int Count)>();
        foreach (var key in _pending.Keys)
            if (_pending.TryRemove(key, out var n)) batch.Add((key.Day, key.Section, n));
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var (day, section, count) in batch)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO daily_visits (day, section, count) VALUES ({day}, {section}, {count})
                    ON CONFLICT (day, section) DO UPDATE SET count = daily_visits.count + excluded.count
                    """, ct);
        }
        catch (Exception ex)
        {
            // Base momentanément indisponible : les totaux sont remis en attente.
            foreach (var (day, section, count) in batch) _pending.AddOrUpdate((day, section), count, (_, n) => n + count);
            logger.LogWarning(ex, "Enregistrement des visites reporté.");
        }
    }
}
