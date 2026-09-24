using Kokora.Domain.Enums;

namespace Kokora.Domain.Rules;

/// <summary>Contexte d'un match en direct, nécessaire pour savoir quelles périodes peuvent suivre.</summary>
public record LiveContext(bool Knockout, bool HasExtraTime, bool HasPenalties, bool IsDraw);

/// <summary>
/// Enchaînement des périodes d'un match en direct :
/// 1re période → mi-temps → 2e période → fin, ou, en élimination directe avec égalité,
/// prolongations (si prévues) puis tirs au but (si prévus).
/// </summary>
public static class LiveFlow
{
    public static IReadOnlyList<LivePeriod> Next(LivePeriod current, LiveContext ctx)
    {
        var tie = ctx.Knockout && ctx.IsDraw;
        return current switch
        {
            LivePeriod.NotStarted => [LivePeriod.FirstHalf],
            LivePeriod.FirstHalf => [LivePeriod.HalfTime],
            LivePeriod.HalfTime => [LivePeriod.SecondHalf],
            LivePeriod.SecondHalf when tie && ctx.HasExtraTime => [LivePeriod.BreakBeforeExtraTime],
            LivePeriod.SecondHalf when tie && ctx.HasPenalties => [LivePeriod.Penalties],
            LivePeriod.SecondHalf => [LivePeriod.Ended],
            LivePeriod.BreakBeforeExtraTime => [LivePeriod.ExtraTimeFirstHalf],
            LivePeriod.ExtraTimeFirstHalf => [LivePeriod.ExtraTimeHalfTime],
            LivePeriod.ExtraTimeHalfTime => [LivePeriod.ExtraTimeSecondHalf],
            LivePeriod.ExtraTimeSecondHalf when tie && ctx.HasPenalties => [LivePeriod.Penalties],
            LivePeriod.ExtraTimeSecondHalf => [LivePeriod.Ended],
            LivePeriod.Penalties => [LivePeriod.Ended],
            _ => []
        };
    }

    /// <summary>Périodes où le chronomètre tourne.</summary>
    public static bool IsRunning(LivePeriod p) =>
        p is LivePeriod.FirstHalf or LivePeriod.SecondHalf or LivePeriod.ExtraTimeFirstHalf or LivePeriod.ExtraTimeSecondHalf;

    /// <summary>Statut public correspondant à la période.</summary>
    public static MatchStatus StatusFor(LivePeriod p) => p switch
    {
        LivePeriod.NotStarted => MatchStatus.Scheduled,
        LivePeriod.HalfTime or LivePeriod.ExtraTimeHalfTime or LivePeriod.BreakBeforeExtraTime => MatchStatus.HalfTime,
        LivePeriod.Ended => MatchStatus.Finished,
        _ => MatchStatus.Live
    };

    /// <summary>Minute de jeu (numérique) : début de période + temps écoulé, avec temps additionnel séparé.</summary>
    public static (int Minute, int? Added) Minute(LivePeriod period, DateTimeOffset? startedAt, int half, int extraHalf, DateTimeOffset now)
    {
        (int Base, int Length) = period switch
        {
            LivePeriod.FirstHalf => (0, half),
            LivePeriod.SecondHalf => (half, half),
            LivePeriod.ExtraTimeFirstHalf => (2 * half, extraHalf),
            LivePeriod.ExtraTimeSecondHalf => (2 * half + extraHalf, extraHalf),
            LivePeriod.HalfTime => (0, half),
            LivePeriod.BreakBeforeExtraTime or LivePeriod.ExtraTimeHalfTime => (half, half),
            _ => (2 * half, 0)
        };
        if (!IsRunning(period)) return (Base + Length, null);
        var elapsed = startedAt is null ? 1 : Math.Max(1, (int)Math.Floor((now - startedAt.Value).TotalMinutes) + 1);
        return elapsed > Length ? (Base + Length, elapsed - Length) : (Base + elapsed, null);
    }

    /// <summary>Minute de départ d'une période de jeu (0 pour la 1re période, 45 pour la 2e…).</summary>
    public static int Start(LivePeriod period, int half, int extraHalf) => period switch
    {
        LivePeriod.SecondHalf => half,
        LivePeriod.ExtraTimeFirstHalf => 2 * half,
        LivePeriod.ExtraTimeSecondHalf => 2 * half + extraHalf,
        _ => 0
    };

    /// <summary>Libellé français d'une période (écran terrain, chronologie).</summary>
    public static string Label(LivePeriod p) => p switch
    {
        LivePeriod.NotStarted => "Avant-match",
        LivePeriod.FirstHalf => "1re période",
        LivePeriod.HalfTime => "Mi-temps",
        LivePeriod.SecondHalf => "2e période",
        LivePeriod.BreakBeforeExtraTime => "Pause avant prolongations",
        LivePeriod.ExtraTimeFirstHalf => "Prolongation, 1re période",
        LivePeriod.ExtraTimeHalfTime => "Mi-temps des prolongations",
        LivePeriod.ExtraTimeSecondHalf => "Prolongation, 2e période",
        LivePeriod.Penalties => "Tirs au but",
        _ => "Terminé"
    };

    /// <summary>Texte du bouton qui fait passer à la période <paramref name="to"/>.</summary>
    public static string ActionLabel(LivePeriod to) => to switch
    {
        LivePeriod.FirstHalf => "Coup d'envoi",
        LivePeriod.HalfTime => "Mi-temps",
        LivePeriod.SecondHalf => "Début de la 2e période",
        LivePeriod.BreakBeforeExtraTime => "Fin du temps réglementaire",
        LivePeriod.ExtraTimeFirstHalf => "Début des prolongations",
        LivePeriod.ExtraTimeHalfTime => "Mi-temps des prolongations",
        LivePeriod.ExtraTimeSecondHalf => "Reprise des prolongations",
        LivePeriod.Penalties => "Tirs au but",
        _ => "Fin du match"
    };
}
