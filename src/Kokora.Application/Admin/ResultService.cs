using System.ComponentModel.DataAnnotations;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public class ResultInput
{
    public int MatchId { get; set; }

    [Display(Name = "Statut")]
    public MatchStatus Status { get; set; } = MatchStatus.Finished;

    [Range(0, 50, ErrorMessage = "Score entre 0 et 50.")] public int? HomeScore { get; set; }
    [Range(0, 50, ErrorMessage = "Score entre 0 et 50.")] public int? AwayScore { get; set; }
    [Range(0, 50, ErrorMessage = "Score entre 0 et 50.")] public int? HomeHalfTimeScore { get; set; }
    [Range(0, 50, ErrorMessage = "Score entre 0 et 50.")] public int? AwayHalfTimeScore { get; set; }

    [Display(Name = "Prolongations jouées")]
    public bool WentToExtraTime { get; set; }

    [Range(0, 50, ErrorMessage = "Entre 0 et 50.")] public int? HomePenalties { get; set; }
    [Range(0, 50, ErrorMessage = "Entre 0 et 50.")] public int? AwayPenalties { get; set; }

    [Display(Name = "Équipe forfait")]
    public int? ForfeitingClubId { get; set; }

    [StringLength(1000)] [Display(Name = "Notes (visibles sur la fiche du match)")]
    public string? Notes { get; set; }

    /// <summary>Buts et cartons (remplacent ceux déjà saisis).</summary>
    public List<ResultEventInput> Events { get; set; } = [];
}

public class ResultEventInput
{
    public MatchEventType Type { get; set; } = MatchEventType.Goal;
    /// <summary>Équipe concernée. Pour un but contre son camp : l'équipe qui en bénéficie.</summary>
    public int? ClubId { get; set; }
    public int? PlayerId { get; set; }
    public int? AssistPlayerId { get; set; }
    [Range(1, 130, ErrorMessage = "Minute entre 1 et 130.")] public int? Minute { get; set; }
    [Range(0, 20)] public int? AddedTime { get; set; }
}

/// <summary>Saisie (rapide) du résultat d'un match déjà joué, et ses conséquences : classements, tableau.</summary>
public class ResultService(IAppDbContext db, CompetitionCache cache, QualificationService qualifications, ICurrentUser user)
{
    public static readonly MatchEventType[] EditableEvents =
        [MatchEventType.Goal, MatchEventType.PenaltyGoal, MatchEventType.OwnGoal, MatchEventType.YellowCard,
         MatchEventType.SecondYellow, MatchEventType.RedCard];

    public static readonly MatchStatus[] ResultStatuses =
        [MatchStatus.Finished, MatchStatus.Forfeit, MatchStatus.UnderReview, MatchStatus.Abandoned, MatchStatus.Replay];

    public async Task<Match> GetAsync(int matchId, CancellationToken ct = default) =>
        await db.Matches
            .Include(m => m.Phase).ThenInclude(p => p.Competition)
            .Include(m => m.HomeClub).Include(m => m.AwayClub).Include(m => m.Group).Include(m => m.Round)
            .Include(m => m.Events.Where(e => !e.IsCancelled))
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == matchId, ct) ?? throw new NotFoundException("Match");

    public static ResultInput ToInput(Match m) => new()
    {
        MatchId = m.Id,
        Status = ResultStatuses.Contains(m.Status) ? m.Status : MatchStatus.Finished,
        HomeScore = m.HomeScore, AwayScore = m.AwayScore,
        HomeHalfTimeScore = m.HomeHalfTimeScore, AwayHalfTimeScore = m.AwayHalfTimeScore,
        WentToExtraTime = m.WentToExtraTime, HomePenalties = m.HomePenalties, AwayPenalties = m.AwayPenalties,
        ForfeitingClubId = m.ForfeitingClubId, Notes = m.Notes,
        Events = m.Events.Where(e => !e.IsCancelled && EditableEvents.Contains(e.Type))
            .OrderBy(e => e.Period).ThenBy(e => e.Minute).ThenBy(e => e.Id)
            .Select(e => new ResultEventInput
            {
                Type = e.Type, ClubId = e.ClubId, PlayerId = e.PlayerId, AssistPlayerId = e.AssistPlayerId,
                Minute = e.Minute, AddedTime = e.AddedTime
            }).ToList()
    };

    public async Task SaveAsync(ResultInput input, CancellationToken ct = default)
    {
        var m = await GetAsync(input.MatchId, ct);
        if (m.HomeClubId is null || m.AwayClubId is null)
            throw new BusinessRuleException("Les deux équipes doivent être désignées avant de saisir un résultat.");
        if (!ResultStatuses.Contains(input.Status))
            throw new BusinessRuleException("Statut invalide pour un résultat.", nameof(input.Status));
        var home = m.HomeClubId.Value;
        var away = m.AwayClubId.Value;
        var rules = m.Phase.Competition.Scoring;
        var knockout = m.Phase.Type == PhaseType.Knockout;

        switch (input.Status)
        {
            case MatchStatus.Forfeit:
                if (input.ForfeitingClubId != home && input.ForfeitingClubId != away)
                    throw new BusinessRuleException("Indiquez l'équipe déclarée forfait.", nameof(input.ForfeitingClubId));
                // Score administratif appliqué selon les règles de la compétition.
                (input.HomeScore, input.AwayScore) = input.ForfeitingClubId == home
                    ? (rules.ForfeitGoalsAgainst, rules.ForfeitGoalsFor)
                    : (rules.ForfeitGoalsFor, rules.ForfeitGoalsAgainst);
                input.HomePenalties = input.AwayPenalties = null;
                input.WentToExtraTime = false;
                break;
            case MatchStatus.Finished or MatchStatus.UnderReview:
                if (input.HomeScore is null || input.AwayScore is null)
                    throw new BusinessRuleException("Saisissez le score des deux équipes.", nameof(input.HomeScore));
                input.ForfeitingClubId = null;
                break;
            default: // Arrêté, À rejouer : le score éventuel est informatif et ne compte pas.
                input.ForfeitingClubId = null;
                break;
        }

        if (input.HomeHalfTimeScore > input.HomeScore || input.AwayHalfTimeScore > input.AwayScore)
            throw new BusinessRuleException("Le score à la mi-temps ne peut pas dépasser le score final.", nameof(input.HomeHalfTimeScore));

        var hasPenalties = input.HomePenalties is not null || input.AwayPenalties is not null;
        if (input.Status is MatchStatus.Finished or MatchStatus.UnderReview && input.HomeScore == input.AwayScore && knockout)
        {
            if (m.Phase.HasPenalties)
            {
                if (input.HomePenalties is null || input.AwayPenalties is null || input.HomePenalties == input.AwayPenalties)
                    throw new BusinessRuleException("Match nul dans un tour à élimination : saisissez les tirs au but (avec un vainqueur).", nameof(input.HomePenalties));
            }
        }
        else if (hasPenalties)
        {
            // Tirs au but seulement pour un nul en élimination directe.
            input.HomePenalties = input.AwayPenalties = null;
        }
        if (!knockout) input.WentToExtraTime = false;

        // Événements : cohérence avec le score.
        // Les lignes laissées vides dans le formulaire sont ignorées.
        var events = input.Events.Where(e => e.ClubId is not null || e.PlayerId is not null).ToList();
        foreach (var e in events)
        {
            if (!EditableEvents.Contains(e.Type)) throw new BusinessRuleException("Type d'événement invalide.");
            if (e.ClubId != home && e.ClubId != away) throw new BusinessRuleException("Chaque but ou carton doit indiquer l'équipe.");
            if (e.Type is not (MatchEventType.Goal or MatchEventType.PenaltyGoal or MatchEventType.OwnGoal) && e.PlayerId is null)
                throw new BusinessRuleException("Indiquez le joueur averti ou exclu.");
        }
        var goals = events.Where(e => e.Type is MatchEventType.Goal or MatchEventType.PenaltyGoal or MatchEventType.OwnGoal).ToList();
        if (goals.Count > 0 && input.Status is MatchStatus.Finished or MatchStatus.UnderReview)
        {
            int homeGoals = goals.Count(g => g.ClubId == home), awayGoals = goals.Count(g => g.ClubId == away);
            if (homeGoals != input.HomeScore || awayGoals != input.AwayScore)
                throw new BusinessRuleException(
                    $"Les buts saisis ({homeGoals}-{awayGoals}) ne correspondent pas au score ({input.HomeScore}-{input.AwayScore}).");
        }

        // Joueurs : ils doivent appartenir à l'effectif de la saison (le buteur d'un CSC appartient à l'équipe adverse).
        var seasonId = m.Phase.Competition.SeasonId;
        var playerIds = events.SelectMany(e => new[] { e.PlayerId, e.AssistPlayerId }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var squads = await db.SquadMembers.Where(s => s.SeasonId == seasonId && playerIds.Contains(s.PlayerId))
            .ToDictionaryAsync(s => s.PlayerId, s => s.ClubId, ct);
        foreach (var e in events)
        {
            var scorerClub = e.Type == MatchEventType.OwnGoal ? (e.ClubId == home ? away : home) : e.ClubId;
            if (e.PlayerId is { } p && squads.TryGetValue(p, out var c) && c != scorerClub)
                throw new BusinessRuleException("Un joueur saisi n'appartient pas à la bonne équipe.");
            if (e.AssistPlayerId is { } a && (a == e.PlayerId || (squads.TryGetValue(a, out var ca) && ca != e.ClubId)))
                throw new BusinessRuleException("Passeur invalide (même joueur que le buteur ou mauvaise équipe).");
        }

        m.Status = input.Status;
        m.HomeScore = input.HomeScore;
        m.AwayScore = input.AwayScore;
        m.HomeHalfTimeScore = input.HomeHalfTimeScore;
        m.AwayHalfTimeScore = input.AwayHalfTimeScore;
        m.WentToExtraTime = input.WentToExtraTime;
        m.HomePenalties = input.HomePenalties;
        m.AwayPenalties = input.AwayPenalties;
        m.ForfeitingClubId = input.ForfeitingClubId;
        m.LivePeriod = LivePeriod.Ended;
        m.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        // Remplace les buts et cartons (les autres événements, ex. remplacements du direct, sont conservés).
        foreach (var old in m.Events.Where(e => EditableEvents.Contains(e.Type)).ToList())
            db.MatchEvents.Remove(old);
        var half = m.Phase.Competition.HalfDurationMinutes;
        foreach (var e in events)
        {
            var minute = e.Minute ?? 0;
            db.MatchEvents.Add(new MatchEvent
            {
                MatchId = m.Id, Type = e.Type, ClubId = e.ClubId, PlayerId = e.PlayerId,
                AssistPlayerId = e.Type is MatchEventType.Goal or MatchEventType.PenaltyGoal ? e.AssistPlayerId : null,
                Minute = minute, AddedTime = e.AddedTime,
                // Minute inconnue (0) : classée en fin de match.
                Period = minute == 0 ? LivePeriod.Ended
                    : minute <= half ? LivePeriod.FirstHalf
                    : minute <= 2 * half ? LivePeriod.SecondHalf
                    : LivePeriod.ExtraTimeFirstHalf,
                CreatedByUserId = user.UserId
            });
        }

        await db.SaveChangesAsync(ct);
        await qualifications.ResolveFromMatchAsync(m.Id, ct);
        cache.Invalidate(m.Phase.CompetitionId);
    }

    /// <summary>Efface le résultat (erreur de saisie) : le match redevient programmé.</summary>
    public async Task ClearAsync(int matchId, CancellationToken ct = default)
    {
        var m = await GetAsync(matchId, ct);
        m.Status = MatchStatus.Scheduled;
        m.HomeScore = m.AwayScore = m.HomeHalfTimeScore = m.AwayHalfTimeScore = m.HomePenalties = m.AwayPenalties = null;
        m.WentToExtraTime = false;
        m.ForfeitingClubId = null;
        m.LivePeriod = LivePeriod.NotStarted;
        m.PeriodStartedAt = null;
        foreach (var e in m.Events.ToList()) db.MatchEvents.Remove(e);
        await db.SaveChangesAsync(ct);
        await qualifications.ResolveFromMatchAsync(m.Id, ct);
        cache.Invalidate(m.Phase.CompetitionId);
    }

    /// <summary>Vainqueur d'un match terminé (tirs au but et forfait compris), ou null.</summary>
    public static int? Winner(Match m)
    {
        if (m.HomeClubId is null || m.AwayClubId is null) return null;
        if (m.Status == MatchStatus.Forfeit && m.ForfeitingClubId is { } f) return f == m.HomeClubId ? m.AwayClubId : m.HomeClubId;
        if (m.Status is not (MatchStatus.Finished or MatchStatus.UnderReview) || m.HomeScore is null || m.AwayScore is null) return null;
        var diff = m.HomeScore.Value - m.AwayScore.Value;
        if (diff == 0 && m.HomePenalties is { } hp && m.AwayPenalties is { } ap) diff = hp - ap;
        return diff > 0 ? m.HomeClubId : diff < 0 ? m.AwayClubId : null;
    }

    public static int? Loser(Match m) => Winner(m) is { } w ? (w == m.HomeClubId ? m.AwayClubId : m.HomeClubId) : null;
}
