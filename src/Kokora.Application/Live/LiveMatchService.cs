using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using Kokora.Application.Public;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;
using Kokora.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Live;

public class LiveEventInput
{
    public MatchEventType Type { get; set; }
    public int ClubId { get; set; }
    public int? PlayerId { get; set; }
    public int? AssistPlayerId { get; set; }
    public int? PlayerOutId { get; set; }
    /// <summary>Minute saisie ; vide = minute du chronomètre.</summary>
    public int? Minute { get; set; }
    public int? AddedTime { get; set; }
    public bool? IsScored { get; set; }
    public string? ClientKey { get; set; }
}

public record LivePlayerVm(int Id, string Name, int? Number, PlayerPosition Position);

public record LiveEventVm(int Id, MatchEventType Type, LivePeriod Period, string Minute, bool IsHome,
    string? Player, string? Assist, string? PlayerOut, bool? IsScored);

public record LiveActionVm(LivePeriod To, string Label);

public record LiveStateVm
{
    public int Id { get; init; }
    public string Url { get; init; } = "";
    public required TeamVm Home { get; init; }
    public required TeamVm Away { get; init; }
    public string Competition { get; init; } = "";
    public string Stage { get; init; } = "";
    public DateTimeOffset? KickoffAt { get; init; }
    public MatchStatus Status { get; init; }
    public LivePeriod Period { get; init; }
    public string PeriodLabel { get; init; } = "";
    public DateTimeOffset? PeriodStartedAt { get; init; }
    public bool Running { get; init; }
    public int HalfMinutes { get; init; }
    public int ExtraHalfMinutes { get; init; }
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public int? HomePenalties { get; init; }
    public int? AwayPenalties { get; init; }
    public IReadOnlyList<LiveActionVm> Next { get; init; } = [];
    public IReadOnlyList<LivePlayerVm> HomeSquad { get; init; } = [];
    public IReadOnlyList<LivePlayerVm> AwaySquad { get; init; } = [];
    public IReadOnlyList<LiveEventVm> Events { get; init; } = [];
    public int? ManOfTheMatchId { get; init; }
}

/// <summary>
/// Mode terrain : le match est suivi en direct depuis le bord du terrain (périodes, buts, cartons, remplacements,
/// tirs au but). Chaque action met à jour le score, invalide les classements et est diffusée aux spectateurs connectés.
/// </summary>
public class LiveMatchService(IAppDbContext db, CompetitionCache cache, QualificationService qualifications,
    ILiveNotifier notifier, ICurrentUser user, Engagement.NotificationService push, Engagement.PredictionService predictions)
{
    private static readonly MatchEventType[] GoalTypes = [MatchEventType.Goal, MatchEventType.PenaltyGoal, MatchEventType.OwnGoal];
    private static readonly MatchEventType[] LiveTypes =
    [
        MatchEventType.Goal, MatchEventType.PenaltyGoal, MatchEventType.OwnGoal, MatchEventType.MissedPenalty,
        MatchEventType.YellowCard, MatchEventType.SecondYellow, MatchEventType.RedCard, MatchEventType.Substitution,
        MatchEventType.ShootoutKick
    ];

    /// <summary>Matchs proposés au mode terrain : en cours, puis ceux d'hier, aujourd'hui et demain.</summary>
    public async Task<List<MatchRowVm>> ListAsync(CancellationToken ct = default)
    {
        var from = KokoraTime.StartOfDayUtc(KokoraTime.Today.AddDays(-1));
        var to = KokoraTime.StartOfDayUtc(KokoraTime.Today.AddDays(2));
        var now = DateTimeOffset.UtcNow;
        var matches = await db.Matches
            .Where(m => m.HomeClubId != null && m.AwayClubId != null
                && (m.Status == MatchStatus.Live || m.Status == MatchStatus.HalfTime
                    || (m.KickoffAt >= from && m.KickoffAt < to && m.Status != MatchStatus.Postponed)))
            .WithRowData().ToListAsync(ct);
        return matches.Select(m => Mapping.Row(m, now))
            .OrderByDescending(r => r.IsLive).ThenBy(r => r.Status == MatchStatus.Finished).ThenBy(r => r.KickoffAt).ToList();
    }

    private async Task<Match> LoadAsync(int id, CancellationToken ct) =>
        await db.Matches
            .Include(m => m.Phase).ThenInclude(p => p.Competition)
            .Include(m => m.HomeClub).Include(m => m.AwayClub).Include(m => m.Group).Include(m => m.Round)
            .Include(m => m.Events.Where(e => !e.IsCancelled))
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("Match");

    public async Task<LiveStateVm> StateAsync(int id, CancellationToken ct = default)
    {
        var m = await LoadAsync(id, ct);
        if (m.HomeClub is null || m.AwayClub is null)
            throw new BusinessRuleException("Les deux équipes doivent être connues pour suivre ce match en direct.");
        var comp = m.Phase.Competition;
        var clubIds = new[] { m.HomeClubId!.Value, m.AwayClubId!.Value };
        var squads = await db.SquadMembers.AsNoTracking().Include(s => s.Player)
            .Where(s => s.SeasonId == comp.SeasonId && clubIds.Contains(s.ClubId)).ToListAsync(ct);
        List<LivePlayerVm> Squad(int clubId) => squads.Where(s => s.ClubId == clubId)
            .OrderBy(s => s.ShirtNumber ?? 999).ThenBy(s => s.Player.LastName)
            .Select(s => new LivePlayerVm(s.PlayerId, s.Player.Nickname ?? $"{s.Player.FirstName} {s.Player.LastName}".Trim(),
                s.ShirtNumber, s.Player.Position)).ToList();

        var active = Active(m).ToList();
        var playerIds = active.SelectMany(e => new[] { e.PlayerId, e.AssistPlayerId, e.PlayerOutId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var names = await db.Players.AsNoTracking().Where(p => playerIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Nickname ?? $"{p.FirstName} {p.LastName}".Trim(), ct);
        string? Name(int? pid) => pid is { } x && names.TryGetValue(x, out var n) ? n : null;

        return new LiveStateVm
        {
            Id = m.Id, Url = Mapping.MatchUrl(m), Home = Mapping.Team(m.HomeClub)!, Away = Mapping.Team(m.AwayClub)!,
            Competition = comp.Name, Stage = Mapping.Stage(m), KickoffAt = m.KickoffAt,
            Status = m.Status, Period = m.LivePeriod, PeriodLabel = LiveFlow.Label(m.LivePeriod),
            PeriodStartedAt = m.PeriodStartedAt, Running = LiveFlow.IsRunning(m.LivePeriod),
            HalfMinutes = comp.HalfDurationMinutes, ExtraHalfMinutes = comp.ExtraTimeHalfDurationMinutes,
            HomeScore = m.HomeScore ?? 0, AwayScore = m.AwayScore ?? 0,
            HomePenalties = m.HomePenalties, AwayPenalties = m.AwayPenalties,
            Next = m.Status is MatchStatus.Scheduled or MatchStatus.Live or MatchStatus.HalfTime
                ? LiveFlow.Next(m.LivePeriod, Context(m)).Select(p => new LiveActionVm(p, LiveFlow.ActionLabel(p))).ToList()
                : [],
            HomeSquad = Squad(m.HomeClubId.Value), AwaySquad = Squad(m.AwayClubId.Value),
            Events = active.Where(e => LiveTypes.Contains(e.Type))
                .OrderByDescending(e => e.Period).ThenByDescending(e => e.Minute).ThenByDescending(e => e.AddedTime ?? 0).ThenByDescending(e => e.Id)
                .Select(e => new LiveEventVm(e.Id, e.Type, e.Period, e.MinuteLabel, e.ClubId == m.HomeClubId,
                    Name(e.PlayerId), Name(e.AssistPlayerId), Name(e.PlayerOutId), e.IsScored))
                .ToList(),
            ManOfTheMatchId = m.ManOfTheMatchPlayerId
        };
    }

    /// <summary>
    /// Actions non annulées. Filtre explicite : dans une même requête, une action annulée reste dans la collection
    /// déjà chargée (le filtre de l'Include ne s'applique qu'au chargement).
    /// </summary>
    private static IEnumerable<MatchEvent> Active(Match m) => m.Events.Where(e => !e.IsCancelled);

    private static LiveContext Context(Match m) => new(
        m.Phase.Type == PhaseType.Knockout, m.Phase.HasExtraTime, m.Phase.HasPenalties,
        (m.HomeScore ?? 0) == (m.AwayScore ?? 0));

    // ------------------------------------------------------------ Périodes

    public async Task AdvanceAsync(int id, LivePeriod to, CancellationToken ct = default)
    {
        var m = await LoadAsync(id, ct);
        if (m.LivePeriod == to) return; // action déjà prise en compte (renvoi depuis un autre appareil)
        if (m.HomeClubId is null || m.AwayClubId is null)
            throw new BusinessRuleException("Les deux équipes doivent être connues pour lancer le match.");
        if (m.LivePeriod == LivePeriod.NotStarted && m.Status is not (MatchStatus.Scheduled or MatchStatus.Postponed or MatchStatus.Replay))
            throw new BusinessRuleException("Ce match a déjà un résultat : effacez-le d'abord pour le suivre en direct.");
        if (!LiveFlow.Next(m.LivePeriod, Context(m)).Contains(to))
            throw new BusinessRuleException($"Action impossible : le match est en « {LiveFlow.Label(m.LivePeriod).ToLower(KokoraTime.Fr)} ».");

        var now = DateTimeOffset.UtcNow;
        switch (to)
        {
            case LivePeriod.FirstHalf:
                m.HomeScore = m.AwayScore = 0;
                m.HomeHalfTimeScore = m.AwayHalfTimeScore = m.HomePenalties = m.AwayPenalties = null;
                m.WentToExtraTime = false;
                m.ForfeitingClubId = null;
                break;
            case LivePeriod.HalfTime:
                m.HomeHalfTimeScore = m.HomeScore;
                m.AwayHalfTimeScore = m.AwayScore;
                break;
            case LivePeriod.ExtraTimeFirstHalf:
                m.WentToExtraTime = true;
                break;
            case LivePeriod.Penalties:
                m.HomePenalties = m.AwayPenalties = 0;
                break;
            case LivePeriod.Ended when m.LivePeriod == LivePeriod.Penalties:
                if (m.HomePenalties == m.AwayPenalties)
                    throw new BusinessRuleException("Les tirs au but doivent désigner un vainqueur avant de terminer le match.");
                break;
        }
        m.LivePeriod = to;
        m.Status = LiveFlow.StatusFor(to);
        m.PeriodStartedAt = to == LivePeriod.Ended ? null : now;
        await SaveAsync(m, to == LivePeriod.Ended ? "Fin du match" : LiveFlow.ActionLabel(to), ct);
        if (to == LivePeriod.FirstHalf) await push.MatchAsync(m, Engagement.MatchPushKind.Kickoff, null, ct);
        if (to == LivePeriod.Ended)
        {
            await qualifications.ResolveFromMatchAsync(m.Id, ct);
            await predictions.ScoreMatchAsync(m.Id, ct);
            await push.MatchAsync(m, Engagement.MatchPushKind.FullTime, $"{m.Phase.Competition.Name} · {Mapping.Stage(m)}", ct);
        }
    }

    /// <summary>Homme du match désigné par l'organisation, en général juste après le coup de sifflet final.</summary>
    public async Task SetManOfTheMatchAsync(int id, int? playerId, CancellationToken ct = default)
    {
        var m = await LoadAsync(id, ct);
        if (m.Status is not (MatchStatus.Finished or MatchStatus.UnderReview or MatchStatus.Live or MatchStatus.HalfTime))
            throw new BusinessRuleException("L'homme du match se désigne pendant ou après le match.");
        if (playerId is { } p && !await db.SquadMembers.AnyAsync(s => s.SeasonId == m.Phase.Competition.SeasonId && s.PlayerId == p
                && (s.ClubId == m.HomeClubId || s.ClubId == m.AwayClubId), ct))
            throw new BusinessRuleException("Ce joueur n'est dans aucune des deux équipes.");
        m.ManOfTheMatchPlayerId = playerId;
        await SaveAsync(m, null, ct);
    }

    /// <summary>Recale le chronomètre sur la minute annoncée par l'arbitre.</summary>
    public async Task SetMinuteAsync(int id, int minute, CancellationToken ct = default)
    {
        var m = await LoadAsync(id, ct);
        if (!LiveFlow.IsRunning(m.LivePeriod)) throw new BusinessRuleException("Le chronomètre ne tourne pas pendant cette période.");
        var comp = m.Phase.Competition;
        var start = LiveFlow.Start(m.LivePeriod, comp.HalfDurationMinutes, comp.ExtraTimeHalfDurationMinutes);
        if (minute <= start || minute > start + 60)
            throw new BusinessRuleException($"Minute invalide pour cette période (à partir de {start + 1}).", nameof(minute));
        m.PeriodStartedAt = DateTimeOffset.UtcNow.AddMinutes(-(minute - start - 1)).AddSeconds(-5);
        await SaveAsync(m, null, ct);
    }

    // ------------------------------------------------------------ Événements

    public async Task AddEventAsync(int id, LiveEventInput input, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(input.ClientKey) ? null : input.ClientKey.Trim()[..Math.Min(40, input.ClientKey.Trim().Length)];
        if (key is not null && await db.MatchEvents.AnyAsync(e => e.MatchId == id && e.ClientKey == key, ct)) return;

        var m = await LoadAsync(id, ct);
        if (m.LivePeriod is LivePeriod.NotStarted or LivePeriod.Ended || !m.IsLive)
            throw new BusinessRuleException("Le match n'est pas en cours.");
        if (!LiveTypes.Contains(input.Type)) throw new BusinessRuleException("Type d'action invalide.");
        if (input.ClubId != m.HomeClubId && input.ClubId != m.AwayClubId) throw new BusinessRuleException("Équipe invalide.");
        var shootout = m.LivePeriod == LivePeriod.Penalties;
        if (shootout != (input.Type == MatchEventType.ShootoutKick))
            throw new BusinessRuleException(shootout ? "Pendant la séance, saisissez des tirs au but." : "Les tirs au but se saisissent pendant la séance.");

        var home = m.HomeClubId!.Value;
        var other = input.ClubId == home ? m.AwayClubId!.Value : home;
        // Le buteur d'un but contre son camp joue dans l'équipe adverse de celle qui en bénéficie.
        var playerClub = input.Type == MatchEventType.OwnGoal ? other : input.ClubId;
        var seasonId = m.Phase.Competition.SeasonId;
        async Task CheckPlayer(int? pid, int clubId)
        {
            if (pid is not { } p) return;
            if (!await db.SquadMembers.AnyAsync(s => s.SeasonId == seasonId && s.PlayerId == p && s.ClubId == clubId, ct))
                throw new BusinessRuleException("Ce joueur n'est pas dans l'effectif de l'équipe.");
        }
        await CheckPlayer(input.PlayerId, playerClub);
        await CheckPlayer(input.AssistPlayerId, input.ClubId);
        await CheckPlayer(input.PlayerOutId, input.ClubId);
        if (input.AssistPlayerId is not null && input.AssistPlayerId == input.PlayerId)
            throw new BusinessRuleException("Le passeur ne peut pas être le buteur.");

        var type = input.Type;
        if (input.PlayerId is { } pl && type is MatchEventType.YellowCard or MatchEventType.SecondYellow or MatchEventType.RedCard)
        {
            var cards = Active(m).Where(e => e.PlayerId == pl).Select(e => e.Type).ToList();
            if (cards.Any(t => t is MatchEventType.SecondYellow or MatchEventType.RedCard))
                throw new BusinessRuleException("Ce joueur a déjà été exclu.");
            // Deuxième jaune : transformé automatiquement en exclusion.
            if (type == MatchEventType.YellowCard && cards.Contains(MatchEventType.YellowCard)) type = MatchEventType.SecondYellow;
        }

        var comp = m.Phase.Competition;
        var (clockMinute, clockAdded) = LiveFlow.Minute(m.LivePeriod, m.PeriodStartedAt, comp.HalfDurationMinutes,
            comp.ExtraTimeHalfDurationMinutes, DateTimeOffset.UtcNow);
        var period = m.LivePeriod switch
        {
            // Carton ou remplacement pendant une pause : rattaché à la période qui vient de se terminer.
            LivePeriod.HalfTime => LivePeriod.FirstHalf,
            LivePeriod.BreakBeforeExtraTime => LivePeriod.SecondHalf,
            LivePeriod.ExtraTimeHalfTime => LivePeriod.ExtraTimeFirstHalf,
            var p => p
        };
        var e = new MatchEvent
        {
            MatchId = m.Id, Type = type, Period = period, ClubId = input.ClubId, PlayerId = input.PlayerId,
            AssistPlayerId = type is MatchEventType.Goal or MatchEventType.PenaltyGoal ? input.AssistPlayerId : null,
            PlayerOutId = type == MatchEventType.Substitution ? input.PlayerOutId : null,
            Minute = shootout ? 120 : Math.Clamp(input.Minute ?? clockMinute, 1, 150),
            AddedTime = shootout ? null : input.Minute is null ? clockAdded : input.AddedTime is > 0 ? input.AddedTime : null,
            IsScored = shootout ? input.IsScored ?? true : null,
            ClientKey = key, CreatedByUserId = user.UserId
        };
        m.Events.Add(e);
        Recompute(m);

        var team = input.ClubId == home ? m.HomeClub!.ShortName : m.AwayClub!.ShortName;
        var text = type switch
        {
            MatchEventType.Goal or MatchEventType.PenaltyGoal or MatchEventType.OwnGoal =>
                $"But pour {team} ({e.MinuteLabel}) : {m.HomeClub!.ShortName} {m.HomeScore}-{m.AwayScore} {m.AwayClub!.ShortName}",
            MatchEventType.RedCard or MatchEventType.SecondYellow => $"Carton rouge pour {team} ({e.MinuteLabel})",
            MatchEventType.ShootoutKick => $"Tirs au but : {m.HomeClub!.ShortName} {m.HomePenalties}-{m.AwayPenalties} {m.AwayClub!.ShortName}",
            _ => null
        };
        await SaveAsync(m, text, ct);
        if (type is MatchEventType.Goal or MatchEventType.PenaltyGoal or MatchEventType.OwnGoal)
        {
            var scorer = input.PlayerId is { } sid ? await db.Players.AsNoTracking().Where(p => p.Id == sid)
                .Select(p => p.Nickname ?? p.FirstName + " " + p.LastName).FirstOrDefaultAsync(ct) : null;
            var how = type switch { MatchEventType.PenaltyGoal => " (penalty)", MatchEventType.OwnGoal => " (contre son camp)", _ => "" };
            await push.MatchAsync(m, Engagement.MatchPushKind.Goal, $"{e.MinuteLabel} · {scorer ?? team}{how}", ct);
        }
    }

    /// <summary>Annule une action (erreur de saisie) : conservée pour l'audit mais retirée du score et de la chronologie.</summary>
    public async Task CancelEventAsync(int id, int eventId, CancellationToken ct = default)
    {
        var m = await LoadAsync(id, ct);
        var e = Active(m).FirstOrDefault(x => x.Id == eventId) ?? throw new NotFoundException("Action");
        e.IsCancelled = true;
        Recompute(m);
        await SaveAsync(m, null, ct);
    }

    /// <summary>Score, score à la mi-temps et tirs au but recalculés à partir des actions non annulées.</summary>
    private static void Recompute(Match m)
    {
        var events = Active(m).ToList();
        int Goals(int clubId, Func<MatchEvent, bool>? filter = null) =>
            events.Count(e => GoalTypes.Contains(e.Type) && e.ClubId == clubId && (filter?.Invoke(e) ?? true));
        var (h, a) = (m.HomeClubId!.Value, m.AwayClubId!.Value);
        m.HomeScore = Goals(h);
        m.AwayScore = Goals(a);
        if (m.LivePeriod >= LivePeriod.HalfTime)
        {
            m.HomeHalfTimeScore = Goals(h, e => e.Period == LivePeriod.FirstHalf);
            m.AwayHalfTimeScore = Goals(a, e => e.Period == LivePeriod.FirstHalf);
        }
        if (m.LivePeriod >= LivePeriod.Penalties)
        {
            m.HomePenalties = events.Count(e => e.Type == MatchEventType.ShootoutKick && e.IsScored == true && e.ClubId == h);
            m.AwayPenalties = events.Count(e => e.Type == MatchEventType.ShootoutKick && e.IsScored == true && e.ClubId == a);
        }
    }

    private async Task SaveAsync(Match m, string? text, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new LiveConflictException();
        }
        cache.Invalidate(m.Phase.CompetitionId);
        var comp = m.Phase.Competition;
        await notifier.MatchUpdatedAsync(new LiveUpdate(m.Id, m.Status, m.LivePeriod, m.HomeScore, m.AwayScore, m.HomePenalties,
            m.AwayPenalties, m.PeriodStartedAt, comp.HalfDurationMinutes, comp.ExtraTimeHalfDurationMinutes, text), ct);
    }
}

/// <summary>Deux appareils ont modifié le match au même instant : l'action doit être renvoyée.</summary>
public class LiveConflictException() : Exception("Le match vient d'être modifié depuis un autre appareil. Nouvel essai…");
