using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public class SeasonAdminService(IAppDbContext db, CompetitionAdminService competitions)
{
    public Task<List<Season>> ListAsync(CancellationToken ct = default) =>
        db.Seasons.AsNoTracking().Include(s => s.Competitions.OrderBy(c => c.Order))
            .OrderByDescending(s => s.Year).ToListAsync(ct);

    public async Task<Season> GetAsync(int id, CancellationToken ct = default) =>
        await db.Seasons.Include(s => s.Competitions.OrderBy(c => c.Order)).ThenInclude(c => c.Phases)
            .FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Saison");

    public Task<Season?> CurrentAsync(CancellationToken ct = default) =>
        db.Seasons.AsNoTracking().OrderByDescending(s => s.IsCurrent).ThenByDescending(s => s.Year).FirstOrDefaultAsync(ct);

    public async Task<int> SaveAsync(SeasonInput input, CancellationToken ct = default)
    {
        if (await db.Seasons.AnyAsync(s => s.Year == input.Year && s.Id != input.Id, ct))
            throw new BusinessRuleException($"La saison {input.Year} existe déjà.", nameof(input.Year));

        var season = input.Id == 0 ? new Season() : await db.Seasons.FindAsync([input.Id], ct) ?? throw new NotFoundException("Saison");
        season.Year = input.Year;
        season.Name = input.Name.Trim();
        if (input.Id == 0) db.Seasons.Add(season);

        var isFirst = !await db.Seasons.AnyAsync(s => s.Id != input.Id, ct);
        if (input.IsCurrent || isFirst) await MakeCurrentAsync(season, ct);
        else season.IsCurrent = false;

        await db.SaveChangesAsync(ct);
        return season.Id;
    }

    public async Task SetCurrentAsync(int id, CancellationToken ct = default)
    {
        var season = await db.Seasons.FindAsync([id], ct) ?? throw new NotFoundException("Saison");
        await MakeCurrentAsync(season, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task MakeCurrentAsync(Season season, CancellationToken ct)
    {
        foreach (var other in await db.Seasons.Where(s => s.IsCurrent && s.Id != season.Id).ToListAsync(ct))
            other.IsCurrent = false;
        season.IsCurrent = true;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var season = await db.Seasons.FindAsync([id], ct) ?? throw new NotFoundException("Saison");
        var played = await db.Matches.AnyAsync(m => m.Phase.Competition.SeasonId == id &&
            (m.Status == MatchStatus.Finished || m.Status == MatchStatus.Forfeit), ct);
        if (played)
            throw new BusinessRuleException("Cette saison contient des matchs joués : elle ne peut pas être supprimée.");
        db.Seasons.Remove(season);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Crée la structure habituelle d'une saison à Nguékokh (tout reste modifiable ensuite) :
    /// Zonales 5A et 5B (poules puis phase finale), 4 Grandes de chaque zone (demi-finales et finale), Coupe du Maire.
    /// </summary>
    public async Task CreateStandardStructureAsync(int seasonId, CancellationToken ct = default)
    {
        var season = await GetAsync(seasonId, ct);
        if (season.Competitions.Count > 0)
            throw new BusinessRuleException("Cette saison contient déjà des compétitions.");

        var templates = new (string Name, string Short, string Color, bool Zonal)[]
        {
            ("Zonale 5A", "5A", "#0E6B3A", true),
            ("Zonale 5B", "5B", "#B5471B", true),
            ("4 Grandes Zone 5A", "4G 5A", "#14213D", false),
            ("4 Grandes Zone 5B", "4G 5B", "#5B3A8C", false),
            ("Coupe du Maire", "Coupe", "#9A7A2E", false),
        };

        foreach (var t in templates)
        {
            var compId = await competitions.SaveAsync(new CompetitionInput
            {
                SeasonId = seasonId, Name = t.Name, ShortName = t.Short, Color = t.Color
            }, ct);

            if (t.Zonal)
            {
                await competitions.SavePhaseAsync(new PhaseInput { CompetitionId = compId, Name = "Phase de poules", Type = PhaseType.League }, ct);
                await competitions.SavePhaseAsync(new PhaseInput { CompetitionId = compId, Name = "Phase finale", Type = PhaseType.Knockout }, ct);
            }
            else
            {
                var phaseId = await competitions.SavePhaseAsync(new PhaseInput
                {
                    CompetitionId = compId, Name = t.Name.StartsWith("4 Grandes") ? "Demi-finales et finale" : "Tableau final",
                    Type = PhaseType.Knockout
                }, ct);
                if (t.Name.StartsWith("4 Grandes"))
                    await competitions.CreateKnockoutBracketAsync(new KnockoutInput { PhaseId = phaseId, TeamCount = 4 }, ct);
            }
        }
    }
}
