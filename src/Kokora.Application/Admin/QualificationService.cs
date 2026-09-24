using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public record QualificationSlotVm(int MatchId, MatchSlot Slot, string MatchLabel, int? QualificationId, string? SourceKey,
    string? ResolvedClub, bool IsManual);

public record SourceOption(string Key, string Label, string Group);

/// <summary>
/// Passage des équipes d'une phase à l'autre : « 1er de la Poule A », « vainqueur de la demi-finale 2 »…
/// Les sources sont codées « g{groupe}:{rang} », « w{match} » (vainqueur) ou « l{match} » (perdant).
/// </summary>
public class QualificationService(IAppDbContext db, StandingsService standings, CompetitionCache cache)
{
    /// <summary>Places du premier tour d'une phase à élimination et leur source actuelle.</summary>
    public async Task<List<QualificationSlotVm>> SlotsAsync(int phaseId, CancellationToken ct = default)
    {
        var firstRound = await db.Rounds.Where(r => r.PhaseId == phaseId).OrderBy(r => r.Order).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        var matches = await db.Matches.AsNoTracking().Include(m => m.HomeClub).Include(m => m.AwayClub)
            .Where(m => m.PhaseId == phaseId && (firstRound == null ? m.RoundId == null : m.RoundId == firstRound))
            .OrderBy(m => m.BracketPosition).ThenBy(m => m.Id).ToListAsync(ct);
        var ids = matches.Select(m => m.Id).ToList();
        var quals = await db.Qualifications.AsNoTracking()
            .Where(q => q.TargetMatchId != null && ids.Contains(q.TargetMatchId.Value)).ToListAsync(ct);

        var result = new List<QualificationSlotVm>();
        foreach (var m in matches)
            foreach (var slot in new[] { MatchSlot.Home, MatchSlot.Away })
            {
                var q = quals.FirstOrDefault(x => x.TargetMatchId == m.Id && x.TargetSlot == slot);
                var club = slot == MatchSlot.Home ? m.HomeClub : m.AwayClub;
                result.Add(new QualificationSlotVm(m.Id, slot, $"Match {m.BracketPosition ?? m.Id} · {(slot == MatchSlot.Home ? "domicile" : "extérieur")}",
                    q?.Id, q is null ? null : Key(q), club?.Name, q?.IsManualOverride ?? false));
            }
        return result;
    }

    /// <summary>Sources possibles : rangs des poules et vainqueurs/perdants des matchs à élimination de la saison.</summary>
    public async Task<List<SourceOption>> SourcesAsync(int phaseId, CancellationToken ct = default)
    {
        var seasonId = await db.Phases.Where(p => p.Id == phaseId).Select(p => p.Competition.SeasonId).FirstAsync(ct);
        var groups = await db.Groups.AsNoTracking().Where(g => g.Phase.Competition.SeasonId == seasonId && g.PhaseId != phaseId)
            .OrderBy(g => g.Phase.Competition.Order).ThenBy(g => g.Phase.Order).ThenBy(g => g.Order)
            .Select(g => new { g.Id, g.Name, Comp = g.Phase.Competition.Name, Size = g.Teams.Count }).ToListAsync(ct);
        var options = new List<SourceOption>();
        foreach (var g in groups)
            for (var rank = 1; rank <= Math.Max(g.Size, 2); rank++)
                options.Add(new SourceOption($"g{g.Id}:{rank}", $"{rank}{(rank == 1 ? "er" : "e")} {g.Name}", g.Comp));

        var knockout = await db.Matches.AsNoTracking()
            .Where(m => m.Phase.Competition.SeasonId == seasonId && m.PhaseId != phaseId && m.RoundId != null)
            .OrderBy(m => m.Phase.Competition.Order).ThenBy(m => m.Round!.Order).ThenBy(m => m.BracketPosition)
            .Select(m => new { m.Id, Round = m.Round!.Name, m.BracketPosition, Comp = m.Phase.Competition.Name }).ToListAsync(ct);
        foreach (var m in knockout)
        {
            var label = $"{m.Round} {m.BracketPosition}";
            options.Add(new SourceOption($"w{m.Id}", $"Vainqueur {label}", m.Comp));
            options.Add(new SourceOption($"l{m.Id}", $"Perdant {label}", m.Comp));
        }
        return options;
    }

    /// <summary>Enregistre la source d'une place (vide = retirer la règle).</summary>
    public async Task SetSourceAsync(int matchId, MatchSlot slot, string? sourceKey, CancellationToken ct = default)
    {
        var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId, ct) ?? throw new NotFoundException("Match");
        var q = await db.Qualifications.FirstOrDefaultAsync(x => x.TargetMatchId == matchId && x.TargetSlot == slot, ct);
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            if (q is not null) db.Qualifications.Remove(q);
            await db.SaveChangesAsync(ct);
            return;
        }
        q ??= db.Qualifications.Add(new Qualification { TargetPhaseId = match.PhaseId, TargetMatchId = matchId, TargetSlot = slot }).Entity;
        q.SourceGroupId = null; q.SourceRank = null; q.SourceMatchId = null;
        q.IsManualOverride = false; q.ClubId = null; q.ResolvedAt = null;
        switch (sourceKey[0])
        {
            case 'g' when sourceKey[1..].Split(':') is [var g, var r] && int.TryParse(g, out var gid) && int.TryParse(r, out var rank):
                q.Source = QualificationSource.GroupRank; q.SourceGroupId = gid; q.SourceRank = rank;
                break;
            case 'w' or 'l' when int.TryParse(sourceKey[1..], out var mid):
                q.Source = sourceKey[0] == 'w' ? QualificationSource.MatchWinner : QualificationSource.MatchLoser;
                q.SourceMatchId = mid;
                break;
            default:
                throw new BusinessRuleException("Source de qualification invalide.");
        }
        // Libellé lisible tant que l'équipe n'est pas connue.
        var label = (await SourcesAsync(match.PhaseId, ct)).FirstOrDefault(o => o.Key == sourceKey)?.Label;
        if (slot == MatchSlot.Home) match.HomePlaceholder = label; else match.AwayPlaceholder = label;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// « Générer la qualification » : place dans les matchs de la phase les équipes selon les classements et résultats actuels.
    /// Les places modifiées à la main et les matchs déjà joués ne sont pas touchés. Renvoie le nombre de places remplies.
    /// </summary>
    public async Task<int> GenerateAsync(int phaseId, CancellationToken ct = default)
    {
        var quals = await db.Qualifications.Include(q => q.TargetMatch)
            .Where(q => q.TargetPhaseId == phaseId && !q.IsManualOverride).ToListAsync(ct);
        var filled = 0;
        var groupCache = new Dictionary<int, List<Domain.Rules.StandingRow>>();
        foreach (var q in quals)
        {
            int? club = null;
            if (q.Source == QualificationSource.GroupRank && q.SourceGroupId is { } gid && q.SourceRank is { } rank)
            {
                if (!groupCache.TryGetValue(gid, out var rows)) groupCache[gid] = rows = await standings.GroupRowsAsync(gid, ct);
                club = rows.FirstOrDefault(r => r.Rank == rank)?.ClubId;
            }
            else if (q.SourceMatchId is { } mid && await db.Matches.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mid, ct) is { } source)
            {
                club = q.Source == QualificationSource.MatchWinner ? ResultService.Winner(source) : ResultService.Loser(source);
            }
            if (Apply(q, club)) filled++;
        }
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(phaseId, ct);
        return filled;
    }

    /// <summary>Après un résultat : fait avancer vainqueur et perdant vers leurs matchs cibles.</summary>
    public async Task ResolveFromMatchAsync(int matchId, CancellationToken ct = default)
    {
        var source = await db.Matches.AsNoTracking().FirstAsync(m => m.Id == matchId, ct);
        var quals = await db.Qualifications.Include(q => q.TargetMatch)
            .Where(q => q.SourceMatchId == matchId && !q.IsManualOverride).ToListAsync(ct);
        if (quals.Count == 0) return;
        foreach (var q in quals)
            Apply(q, q.Source == QualificationSource.MatchWinner ? ResultService.Winner(source) : ResultService.Loser(source));
        await db.SaveChangesAsync(ct);
        foreach (var phaseId in quals.Select(q => q.TargetPhaseId).Distinct()) await InvalidateAsync(phaseId, ct);
    }

    /// <summary>Quand l'admin choisit lui-même une équipe sur une place alimentée par une règle : on garde son choix.</summary>
    public async Task MarkManualAsync(int matchId, int? homeClubId, int? awayClubId, CancellationToken ct = default)
    {
        var quals = await db.Qualifications.Where(q => q.TargetMatchId == matchId).ToListAsync(ct);
        foreach (var q in quals)
        {
            var chosen = q.TargetSlot == MatchSlot.Home ? homeClubId : awayClubId;
            if (chosen is not null && chosen != q.ClubId) { q.ClubId = chosen; q.IsManualOverride = true; q.ResolvedAt = DateTimeOffset.UtcNow; }
            else if (chosen is null && q.IsManualOverride) q.IsManualOverride = false;
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool Apply(Qualification q, int? club)
    {
        q.ClubId = club;
        q.ResolvedAt = club is null ? null : DateTimeOffset.UtcNow;
        var target = q.TargetMatch;
        if (target is null || target.Status is not (MatchStatus.Scheduled or MatchStatus.Postponed)) return false;
        if (q.TargetSlot == MatchSlot.Home) target.HomeClubId = club; else target.AwayClubId = club;
        return club is not null;
    }

    private async Task InvalidateAsync(int phaseId, CancellationToken ct)
    {
        var compId = await db.Phases.Where(p => p.Id == phaseId).Select(p => p.CompetitionId).FirstAsync(ct);
        cache.Invalidate(compId);
    }

    private static string? Key(Qualification q) => q.Source switch
    {
        QualificationSource.GroupRank => $"g{q.SourceGroupId}:{q.SourceRank}",
        QualificationSource.MatchWinner => $"w{q.SourceMatchId}",
        QualificationSource.MatchLoser => $"l{q.SourceMatchId}",
        _ => null
    };
}
