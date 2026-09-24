using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public class CompetitionAdminService(IAppDbContext db)
{
    // ------------------------------------------------------------------ Compétitions

    public async Task<Competition> GetAsync(int id, CancellationToken ct = default) =>
        await db.Competitions
            .Include(c => c.Season)
            .Include(c => c.Phases.OrderBy(p => p.Order)).ThenInclude(p => p.Groups.OrderBy(g => g.Order)).ThenInclude(g => g.Teams).ThenInclude(t => t.Club)
            .Include(c => c.Phases).ThenInclude(p => p.Rounds.OrderBy(r => r.Order))
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Compétition");

    public static CompetitionInput ToInput(Competition c) => new()
    {
        Id = c.Id, SeasonId = c.SeasonId, Name = c.Name, ShortName = c.ShortName, Color = c.Color,
        Description = c.Description, HalfDurationMinutes = c.HalfDurationMinutes,
        ExtraTimeHalfDurationMinutes = c.ExtraTimeHalfDurationMinutes, IsPublished = c.IsPublished,
        PointsForWin = c.Scoring.PointsForWin, PointsForDraw = c.Scoring.PointsForDraw, PointsForLoss = c.Scoring.PointsForLoss,
        PointsForForfeitLoss = c.Scoring.PointsForForfeitLoss, ForfeitGoalsFor = c.Scoring.ForfeitGoalsFor,
        ForfeitGoalsAgainst = c.Scoring.ForfeitGoalsAgainst, TieBreakers = [.. c.Scoring.TieBreakers],
        SuspensionsEnabled = c.Suspensions.Enabled, YellowCardsThreshold = c.Suspensions.YellowCardsThreshold,
        MatchesForYellowAccumulation = c.Suspensions.MatchesForYellowAccumulation,
        MatchesForSecondYellow = c.Suspensions.MatchesForSecondYellow, MatchesForDirectRed = c.Suspensions.MatchesForDirectRed,
        ResetYellowsEachPhase = c.Suspensions.ResetYellowsEachPhase
    };

    public async Task<int> SaveAsync(CompetitionInput input, CancellationToken ct = default)
    {
        if (!await db.Seasons.AnyAsync(s => s.Id == input.SeasonId, ct)) throw new NotFoundException("Saison");
        if (input.ForfeitGoalsFor <= input.ForfeitGoalsAgainst)
            throw new BusinessRuleException("Le score de forfait doit donner la victoire à l'adversaire.", nameof(input.ForfeitGoalsFor));

        var c = input.Id == 0 ? new Competition { SeasonId = input.SeasonId }
            : await db.Competitions.FirstOrDefaultAsync(x => x.Id == input.Id, ct) ?? throw new NotFoundException("Compétition");

        c.Name = input.Name.Trim();
        c.ShortName = input.ShortName.Trim();
        c.Color = input.Color.ToUpperInvariant();
        c.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        c.HalfDurationMinutes = input.HalfDurationMinutes;
        c.ExtraTimeHalfDurationMinutes = input.ExtraTimeHalfDurationMinutes;
        c.IsPublished = input.IsPublished;

        // Nouveaux objets pour que EF détecte le changement des colonnes JSON.
        c.Scoring = new ScoringRules
        {
            PointsForWin = input.PointsForWin, PointsForDraw = input.PointsForDraw, PointsForLoss = input.PointsForLoss,
            PointsForForfeitLoss = input.PointsForForfeitLoss, ForfeitGoalsFor = input.ForfeitGoalsFor,
            ForfeitGoalsAgainst = input.ForfeitGoalsAgainst,
            TieBreakers = input.TieBreakers.Distinct().Where(Enum.IsDefined).ToList(),
            FairPlayYellowPoints = c.Scoring?.FairPlayYellowPoints ?? 1,
            FairPlaySecondYellowPoints = c.Scoring?.FairPlaySecondYellowPoints ?? 3,
            FairPlayRedPoints = c.Scoring?.FairPlayRedPoints ?? 4
        };
        c.Suspensions = new SuspensionRules
        {
            Enabled = input.SuspensionsEnabled, YellowCardsThreshold = input.YellowCardsThreshold,
            MatchesForYellowAccumulation = input.MatchesForYellowAccumulation,
            MatchesForSecondYellow = input.MatchesForSecondYellow, MatchesForDirectRed = input.MatchesForDirectRed,
            ResetYellowsEachPhase = input.ResetYellowsEachPhase
        };

        if (input.Id == 0)
        {
            c.Slug = await Slug.UniqueAsync(input.Name,
                s => db.Competitions.AnyAsync(x => x.SeasonId == input.SeasonId && x.Slug == s, ct));
            c.Order = await db.Competitions.Where(x => x.SeasonId == input.SeasonId).MaxAsync(x => (int?)x.Order, ct) + 1 ?? 1;
            db.Competitions.Add(c);
        }
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var c = await db.Competitions.FindAsync([id], ct) ?? throw new NotFoundException("Compétition");
        if (await db.Matches.AnyAsync(m => m.Phase.CompetitionId == id && (m.Status == MatchStatus.Finished || m.Status == MatchStatus.Forfeit), ct))
            throw new BusinessRuleException("Cette compétition contient des matchs joués : masquez-la plutôt que de la supprimer.");
        db.Competitions.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(int seasonId, IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        var items = await db.Competitions.Where(c => c.SeasonId == seasonId).ToListAsync(ct);
        ApplyOrder(items, orderedIds, (c, o) => c.Order = o);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ Phases

    public async Task<Phase> GetPhaseAsync(int id, CancellationToken ct = default) =>
        await db.Phases
            .Include(p => p.Competition).ThenInclude(c => c.Season)
            .Include(p => p.Groups.OrderBy(g => g.Order)).ThenInclude(g => g.Teams.OrderBy(t => t.Seed)).ThenInclude(t => t.Club)
            .Include(p => p.Rounds.OrderBy(r => r.Order))
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Phase");

    public async Task<int> SavePhaseAsync(PhaseInput input, CancellationToken ct = default)
    {
        var p = input.Id == 0 ? new Phase { CompetitionId = input.CompetitionId }
            : await db.Phases.FindAsync([input.Id], ct) ?? throw new NotFoundException("Phase");

        if (input.Id != 0 && p.Type != input.Type && await db.Matches.AnyAsync(m => m.PhaseId == p.Id, ct))
            throw new BusinessRuleException("Impossible de changer le type d'une phase qui contient des matchs.", nameof(input.Type));

        p.Name = input.Name.Trim();
        p.Type = input.Type;
        p.Legs = input.Legs;
        p.HasExtraTime = input.Type == PhaseType.Knockout && input.HasExtraTime;
        p.HasPenalties = input.Type == PhaseType.Knockout && input.HasPenalties;

        if (input.Id == 0)
        {
            if (!await db.Competitions.AnyAsync(c => c.Id == input.CompetitionId, ct)) throw new NotFoundException("Compétition");
            p.Order = await db.Phases.Where(x => x.CompetitionId == input.CompetitionId).MaxAsync(x => (int?)x.Order, ct) + 1 ?? 1;
            db.Phases.Add(p);
        }
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task DeletePhaseAsync(int id, CancellationToken ct = default)
    {
        var p = await db.Phases.FindAsync([id], ct) ?? throw new NotFoundException("Phase");
        if (await db.Matches.AnyAsync(m => m.PhaseId == id && (m.Status == MatchStatus.Finished || m.Status == MatchStatus.Forfeit), ct))
            throw new BusinessRuleException("Cette phase contient des matchs joués : elle ne peut pas être supprimée.");
        // Les qualifications qui pointent vers les matchs de cette phase sont supprimées avec eux.
        db.Qualifications.RemoveRange(db.Qualifications.Where(q => q.TargetPhaseId == id || q.SourceMatch!.PhaseId == id));
        db.Phases.Remove(p);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderPhasesAsync(int competitionId, IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        var items = await db.Phases.Where(p => p.CompetitionId == competitionId).ToListAsync(ct);
        ApplyOrder(items, orderedIds, (p, o) => p.Order = o);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ Poules

    public static GroupInput ToInput(Group g)
    {
        var q = g.Zones.FirstOrDefault(z => z.Kind == StandingZoneKind.Qualified);
        var p = g.Zones.FirstOrDefault(z => z.Kind == StandingZoneKind.Playoff);
        var e = g.Zones.FirstOrDefault(z => z.Kind == StandingZoneKind.Eliminated);
        return new GroupInput
        {
            Id = g.Id, PhaseId = g.PhaseId, Name = g.Name,
            ClubIds = g.Teams.OrderBy(t => t.Seed).Select(t => t.ClubId).ToList(),
            QualifiedCount = q is null ? 0 : q.ToRank - q.FromRank + 1,
            QualifiedLabel = q?.Label ?? "Qualifié",
            PlayoffCount = p is null ? 0 : p.ToRank - p.FromRank + 1,
            EliminatedCount = e is null ? 0 : e.ToRank - e.FromRank + 1
        };
    }

    public async Task<int> SaveGroupAsync(GroupInput input, CancellationToken ct = default)
    {
        var phase = await db.Phases.Include(p => p.Competition).FirstOrDefaultAsync(p => p.Id == input.PhaseId, ct)
            ?? throw new NotFoundException("Phase");
        if (phase.Type != PhaseType.League)
            throw new BusinessRuleException("Les poules n'existent que dans une phase de championnat.");

        var clubIds = input.ClubIds.Distinct().ToList();
        var total = input.QualifiedCount + input.PlayoffCount + input.EliminatedCount;
        if (clubIds.Count > 0 && total > clubIds.Count)
            throw new BusinessRuleException("Plus de places qualificatives que d'équipes dans la poule.", nameof(input.QualifiedCount));

        // Une équipe ne peut être que dans une poule par phase.
        var elsewhere = await db.GroupTeams
            .Where(t => t.Group.PhaseId == input.PhaseId && t.GroupId != input.Id && clubIds.Contains(t.ClubId))
            .Select(t => t.Club.Name + " (" + t.Group.Name + ")").ToListAsync(ct);
        if (elsewhere.Count > 0)
            throw new BusinessRuleException($"Déjà dans une autre poule : {string.Join(", ", elsewhere)}.", nameof(input.ClubIds));

        var g = input.Id == 0 ? new Group { PhaseId = input.PhaseId }
            : await db.Groups.Include(x => x.Teams).FirstOrDefaultAsync(x => x.Id == input.Id, ct) ?? throw new NotFoundException("Poule");

        // On ne retire pas une équipe qui a déjà des matchs dans cette poule.
        var removed = g.Teams.Where(t => !clubIds.Contains(t.ClubId)).Select(t => t.ClubId).ToList();
        if (removed.Count > 0 && await db.Matches.AnyAsync(m => m.GroupId == g.Id &&
                (removed.Contains(m.HomeClubId!.Value) || removed.Contains(m.AwayClubId!.Value)), ct))
            throw new BusinessRuleException("Une équipe retirée a déjà des matchs dans cette poule : supprimez d'abord ses matchs.", nameof(input.ClubIds));

        g.Name = input.Name.Trim();
        g.Teams.RemoveAll(t => !clubIds.Contains(t.ClubId));
        for (var i = 0; i < clubIds.Count; i++)
        {
            var existing = g.Teams.FirstOrDefault(t => t.ClubId == clubIds[i]);
            if (existing is null) g.Teams.Add(new GroupTeam { ClubId = clubIds[i], Seed = i + 1 });
            else existing.Seed = i + 1;
        }

        var zones = new List<StandingZone>();
        if (input.QualifiedCount > 0)
            zones.Add(new StandingZone { FromRank = 1, ToRank = input.QualifiedCount, Kind = StandingZoneKind.Qualified, Label = input.QualifiedLabel?.Trim() is { Length: > 0 } l ? l : "Qualifié" });
        if (input.PlayoffCount > 0)
            zones.Add(new StandingZone { FromRank = input.QualifiedCount + 1, ToRank = input.QualifiedCount + input.PlayoffCount, Kind = StandingZoneKind.Playoff, Label = "Barrage" });
        if (input.EliminatedCount > 0)
        {
            var size = Math.Max(clubIds.Count, total);
            zones.Add(new StandingZone { FromRank = size - input.EliminatedCount + 1, ToRank = size, Kind = StandingZoneKind.Eliminated, Label = "Éliminé" });
        }
        g.Zones = zones;

        if (input.Id == 0)
        {
            g.Order = await db.Groups.Where(x => x.PhaseId == input.PhaseId).MaxAsync(x => (int?)x.Order, ct) + 1 ?? 1;
            db.Groups.Add(g);
        }
        await db.SaveChangesAsync(ct);
        return g.Id;
    }

    public async Task DeleteGroupAsync(int id, CancellationToken ct = default)
    {
        var g = await db.Groups.FindAsync([id], ct) ?? throw new NotFoundException("Poule");
        if (await db.Matches.AnyAsync(m => m.GroupId == id, ct))
            throw new BusinessRuleException("Cette poule contient des matchs : supprimez-les d'abord.");
        db.Groups.Remove(g);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ Élimination directe

    public static readonly int[] BracketSizes = [2, 4, 8, 16, 32];

    /// <summary>
    /// Crée les tours d'une phase à élimination et leurs matchs (équipes à désigner),
    /// reliés entre eux : le vainqueur de chaque match est qualifié automatiquement pour le tour suivant.
    /// </summary>
    public async Task CreateKnockoutBracketAsync(KnockoutInput input, CancellationToken ct = default)
    {
        if (!BracketSizes.Contains(input.TeamCount))
            throw new BusinessRuleException("Le tableau doit compter 2, 4, 8, 16 ou 32 équipes.", nameof(input.TeamCount));
        var phase = await db.Phases.Include(p => p.Rounds).FirstOrDefaultAsync(p => p.Id == input.PhaseId, ct)
            ?? throw new NotFoundException("Phase");
        if (phase.Type != PhaseType.Knockout)
            throw new BusinessRuleException("Le tableau n'existe que dans une phase à élimination.");
        if (phase.Rounds.Count > 0)
            throw new BusinessRuleException("Cette phase a déjà des tours. Supprimez-les avant de recréer le tableau.");

        Match[]? previous = null;
        string? previousAbbr = null;
        Match[]? semiFinals = null;
        var order = 1;

        for (var size = input.TeamCount; size >= 2; size /= 2)
        {
            var (kind, name, abbr) = RoundInfo(size);
            var round = new Round { Phase = phase, Name = name, Kind = kind, Order = order++ };
            db.Rounds.Add(round);

            var matches = new Match[size / 2];
            for (var i = 0; i < matches.Length; i++)
            {
                matches[i] = new Match
                {
                    Phase = phase, Round = round, BracketPosition = i + 1,
                    HomePlaceholder = previous is null ? "À désigner" : $"Vainqueur {previousAbbr}{i * 2 + 1}",
                    AwayPlaceholder = previous is null ? "À désigner" : $"Vainqueur {previousAbbr}{i * 2 + 2}"
                };
                db.Matches.Add(matches[i]);
            }

            if (previous is not null)
                for (var i = 0; i < previous.Length; i++)
                    db.Qualifications.Add(new Qualification
                    {
                        Source = QualificationSource.MatchWinner, SourceMatch = previous[i],
                        TargetPhase = phase, TargetMatch = matches[i / 2],
                        TargetSlot = i % 2 == 0 ? MatchSlot.Home : MatchSlot.Away
                    });

            if (kind == RoundKind.SemiFinal) semiFinals = matches;
            previous = matches;
            previousAbbr = abbr;
        }

        if (input.ThirdPlace && semiFinals is not null)
        {
            var round = new Round { Phase = phase, Name = "Match pour la 3e place", Kind = RoundKind.ThirdPlace, Order = order };
            db.Rounds.Add(round);
            var match = new Match
            {
                Phase = phase, Round = round, BracketPosition = 1,
                HomePlaceholder = "Perdant DF1", AwayPlaceholder = "Perdant DF2"
            };
            db.Matches.Add(match);
            for (var i = 0; i < 2; i++)
                db.Qualifications.Add(new Qualification
                {
                    Source = QualificationSource.MatchLoser, SourceMatch = semiFinals[i], TargetPhase = phase,
                    TargetMatch = match, TargetSlot = i == 0 ? MatchSlot.Home : MatchSlot.Away
                });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Supprime tous les tours (et leurs matchs non joués) d'une phase à élimination.</summary>
    public async Task ClearKnockoutBracketAsync(int phaseId, CancellationToken ct = default)
    {
        var matches = await db.Matches.Where(m => m.PhaseId == phaseId && m.RoundId != null).ToListAsync(ct);
        if (matches.Any(m => m.Status is MatchStatus.Finished or MatchStatus.Forfeit or MatchStatus.Live or MatchStatus.HalfTime))
            throw new BusinessRuleException("Des matchs du tableau ont déjà été joués : le tableau ne peut pas être supprimé.");
        var ids = matches.Select(m => m.Id).ToList();
        db.Qualifications.RemoveRange(db.Qualifications.Where(q =>
            (q.SourceMatchId != null && ids.Contains(q.SourceMatchId.Value)) || (q.TargetMatchId != null && ids.Contains(q.TargetMatchId.Value))));
        db.Matches.RemoveRange(matches);
        db.Rounds.RemoveRange(db.Rounds.Where(r => r.PhaseId == phaseId));
        await db.SaveChangesAsync(ct);
    }

    public static (RoundKind Kind, string Name, string Abbr) RoundInfo(int teamsInRound) => teamsInRound switch
    {
        32 => (RoundKind.RoundOf32, "Seizièmes de finale", "1/16 n°"),
        16 => (RoundKind.RoundOf16, "Huitièmes de finale", "1/8 n°"),
        8 => (RoundKind.QuarterFinal, "Quarts de finale", "QF"),
        4 => (RoundKind.SemiFinal, "Demi-finales", "DF"),
        2 => (RoundKind.Final, "Finale", "F"),
        _ => (RoundKind.Other, "Tour", "T")
    };

    private static void ApplyOrder<T>(List<T> items, IReadOnlyList<int> orderedIds, Action<T, int> set) where T : Domain.Common.Entity
    {
        var position = orderedIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        foreach (var item in items)
            if (position.TryGetValue(item.Id, out var o)) set(item, o);
    }
}
