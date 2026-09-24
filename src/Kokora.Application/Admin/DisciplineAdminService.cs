using System.ComponentModel.DataAnnotations;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Competitions;
using Kokora.Domain.Discipline;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public class SuspensionInput
{
    [Required(ErrorMessage = "Choisissez le joueur.")] [Display(Name = "Joueur")]
    public int? PlayerId { get; set; }

    [Required(ErrorMessage = "Choisissez la compétition.")] [Display(Name = "Compétition")]
    public int? CompetitionId { get; set; }

    [Range(1, 50, ErrorMessage = "Entre 1 et 50 matchs.")] [Display(Name = "Nombre de matchs")]
    public int Matches { get; set; } = 1;

    [Required(ErrorMessage = "Indiquez le motif.")] [StringLength(500)] [Display(Name = "Motif")]
    public string Reason { get; set; } = "";

    [Display(Name = "Date de la décision")]
    public DateOnly? DecidedOn { get; set; }
}

public class PointAdjustmentInput
{
    [Required(ErrorMessage = "Choisissez la poule.")] [Display(Name = "Poule")]
    public int? GroupId { get; set; }

    [Required(ErrorMessage = "Choisissez l'équipe.")] [Display(Name = "Équipe")]
    public int? ClubId { get; set; }

    [Range(-50, 50, ErrorMessage = "Entre -50 et 50.")] [Display(Name = "Points (négatif = pénalité)")]
    public int Points { get; set; } = -3;

    [Required(ErrorMessage = "Indiquez le motif.")] [StringLength(500)] [Display(Name = "Motif")]
    public string Reason { get; set; } = "";

    [Display(Name = "Date de la décision")]
    public DateOnly? DecidedOn { get; set; }
}

public record ManualSuspensionItem(int Id, string Player, string Club, string Competition, int Matches, string Reason, DateOnly DecidedOn);
public record PointAdjustmentItem(int Id, string Club, string Group, string Competition, int Points, string Reason, DateOnly DecidedOn);

/// <summary>Décisions de la commission : suspensions manuelles et pénalités de points. Classements recalculés aussitôt.</summary>
public class DisciplineAdminService(IAppDbContext db, CompetitionCache cache)
{
    public Task<List<ManualSuspensionItem>> SuspensionsAsync(int seasonId, CancellationToken ct = default) =>
        db.Suspensions.AsNoTracking().Where(s => s.Competition.SeasonId == seasonId)
            .OrderByDescending(s => s.DecidedOn).ThenByDescending(s => s.Id)
            .Select(s => new ManualSuspensionItem(s.Id, s.Player.FirstName + " " + s.Player.LastName, s.Club != null ? s.Club.Name : "?",
                s.Competition.Name, s.Matches, s.Reason, s.DecidedOn))
            .ToListAsync(ct);

    public Task<List<PointAdjustmentItem>> AdjustmentsAsync(int seasonId, CancellationToken ct = default) =>
        db.PointAdjustments.AsNoTracking().Where(a => a.Group.Phase.Competition.SeasonId == seasonId)
            .OrderByDescending(a => a.DecidedOn).ThenByDescending(a => a.Id)
            .Select(a => new PointAdjustmentItem(a.Id, a.Club.Name, a.Group.Phase.Name + " · " + a.Group.Name, a.Group.Phase.Competition.Name,
                a.Points, a.Reason, a.DecidedOn))
            .ToListAsync(ct);

    public async Task AddSuspensionAsync(SuspensionInput input, int seasonId, CancellationToken ct = default)
    {
        var comp = await db.Competitions.FirstOrDefaultAsync(c => c.Id == input.CompetitionId && c.SeasonId == seasonId, ct)
            ?? throw new BusinessRuleException("Compétition introuvable pour cette saison.", nameof(input.CompetitionId));
        var clubId = await db.SquadMembers.Where(s => s.PlayerId == input.PlayerId && s.SeasonId == seasonId)
            .Select(s => (int?)s.ClubId).FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("Ce joueur n'est inscrit dans aucune équipe cette saison.", nameof(input.PlayerId));
        db.Suspensions.Add(new Suspension
        {
            PlayerId = input.PlayerId!.Value, CompetitionId = comp.Id, ClubId = clubId, Matches = input.Matches,
            Reason = input.Reason.Trim(), DecidedOn = input.DecidedOn ?? KokoraTime.Today
        });
        await db.SaveChangesAsync(ct);
        cache.Invalidate(comp.Id);
    }

    public async Task DeleteSuspensionAsync(int id, CancellationToken ct = default)
    {
        var s = await db.Suspensions.FindAsync([id], ct) ?? throw new NotFoundException("Suspension");
        db.Suspensions.Remove(s);
        await db.SaveChangesAsync(ct);
        cache.Invalidate(s.CompetitionId);
    }

    public async Task AddAdjustmentAsync(PointAdjustmentInput input, CancellationToken ct = default)
    {
        var group = await db.Groups.Include(g => g.Teams).Include(g => g.Phase)
            .FirstOrDefaultAsync(g => g.Id == input.GroupId, ct) ?? throw new NotFoundException("Poule");
        if (input.Points == 0) throw new BusinessRuleException("Indiquez un nombre de points.", nameof(input.Points));
        if (group.Teams.All(t => t.ClubId != input.ClubId))
            throw new BusinessRuleException("Cette équipe ne fait pas partie de la poule.", nameof(input.ClubId));
        db.PointAdjustments.Add(new PointAdjustment
        {
            GroupId = group.Id, ClubId = input.ClubId!.Value, Points = input.Points, Reason = input.Reason.Trim(),
            DecidedOn = input.DecidedOn ?? KokoraTime.Today
        });
        await db.SaveChangesAsync(ct);
        cache.Invalidate(group.Phase.CompetitionId);
    }

    public async Task DeleteAdjustmentAsync(int id, CancellationToken ct = default)
    {
        var a = await db.PointAdjustments.Include(x => x.Group).ThenInclude(g => g.Phase).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Pénalité");
        db.PointAdjustments.Remove(a);
        await db.SaveChangesAsync(ct);
        cache.Invalidate(a.Group.Phase.CompetitionId);
    }
}
