using Kokora.Domain.Enums;

namespace Kokora.Domain.Rules;

/// <summary>Carton reçu par un joueur dans un match joué de la compétition.</summary>
public readonly record struct CardRecord(int PlayerId, int ClubId, int MatchId, int PhaseId, DateTimeOffset PlayedAt, MatchEventType Type);

/// <summary>Suspension décidée par la commission (en plus des suspensions automatiques).</summary>
public readonly record struct ManualSuspension(int PlayerId, int ClubId, int Matches, DateTimeOffset From, string Reason);

public enum SuspensionReason { YellowAccumulation, SecondYellow, DirectRed, Commission }

public record SuspensionStatus(int PlayerId, int ClubId, SuspensionReason Reason, string? Detail, int Matches, int Served,
    DateTimeOffset TriggeredAt, int? TriggerMatchId)
{
    public int Remaining => Math.Max(0, Matches - Served);
    public bool IsActive => Remaining > 0;
}

public record DisciplineReport(IReadOnlyList<SuspensionStatus> Suspensions, IReadOnlyDictionary<int, int> PendingYellows)
{
    public IEnumerable<SuspensionStatus> Active => Suspensions.Where(s => s.IsActive);
}

/// <summary>
/// Calcule les suspensions d'une compétition à partir des cartons, selon les règles :
/// cumul de jaunes (le compteur repart à zéro après chaque suspension), 2e jaune, rouge direct,
/// plus les suspensions manuelles. Une suspension est purgée par les matchs suivants joués par l'équipe du joueur.
/// Les deux jaunes d'un même match qui mènent à l'exclusion ne comptent pas dans le cumul.
/// </summary>
public static class SuspensionCalculator
{
    public static DisciplineReport Compute(
        SuspensionRules rules,
        IEnumerable<CardRecord> cards,
        IReadOnlyDictionary<int, List<(int MatchId, DateTimeOffset PlayedAt)>> playedByClub,
        IEnumerable<ManualSuspension>? manual = null)
    {
        var suspensions = new List<(int Player, int Club, SuspensionReason Reason, string? Detail, int Matches, DateTimeOffset At, int? MatchId)>();
        var pending = new Dictionary<int, int>();

        if (rules.Enabled)
        {
            foreach (var player in cards.GroupBy(c => c.PlayerId))
            {
                var yellows = 0;
                int? lastPhase = null;
                foreach (var match in player.GroupBy(c => (c.MatchId, c.PlayedAt, c.PhaseId, c.ClubId)).OrderBy(g => g.Key.PlayedAt))
                {
                    if (rules.ResetYellowsEachPhase && lastPhase is not null && lastPhase != match.Key.PhaseId) yellows = 0;
                    lastPhase = match.Key.PhaseId;

                    var types = match.Select(c => c.Type).ToList();
                    var (club, at, matchId) = (match.Key.ClubId, match.Key.PlayedAt, match.Key.MatchId);
                    if (types.Contains(MatchEventType.RedCard))
                    {
                        // Un jaune reçu avant un rouge direct dans le même match compte quand même.
                        yellows += types.Count(t => t == MatchEventType.YellowCard);
                        if (rules.MatchesForDirectRed > 0)
                            suspensions.Add((player.Key, club, SuspensionReason.DirectRed, null, rules.MatchesForDirectRed, at, matchId));
                    }
                    else if (types.Contains(MatchEventType.SecondYellow))
                    {
                        if (rules.MatchesForSecondYellow > 0)
                            suspensions.Add((player.Key, club, SuspensionReason.SecondYellow, null, rules.MatchesForSecondYellow, at, matchId));
                        continue; // les jaunes de l'exclusion ne s'ajoutent pas au cumul
                    }
                    else
                    {
                        yellows += types.Count(t => t == MatchEventType.YellowCard);
                    }

                    if (rules.YellowCardsThreshold > 0 && yellows >= rules.YellowCardsThreshold)
                    {
                        if (rules.MatchesForYellowAccumulation > 0)
                            suspensions.Add((player.Key, club, SuspensionReason.YellowAccumulation,
                                $"{rules.YellowCardsThreshold} cartons jaunes", rules.MatchesForYellowAccumulation, at, matchId));
                        yellows -= rules.YellowCardsThreshold;
                    }
                }
                if (yellows > 0) pending[player.Key] = yellows;
            }
        }

        foreach (var m in manual ?? [])
            suspensions.Add((m.PlayerId, m.ClubId, SuspensionReason.Commission, m.Reason, m.Matches, m.From, null));

        // Purge : chaque suspension consomme les matchs suivants de l'équipe, après la précédente (elles s'enchaînent).
        var result = new List<SuspensionStatus>();
        foreach (var player in suspensions.GroupBy(s => s.Player))
        {
            var used = new HashSet<int>();
            foreach (var s in player.OrderBy(s => s.At))
            {
                var next = (playedByClub.GetValueOrDefault(s.Club) ?? [])
                    .Where(m => m.PlayedAt > s.At && m.MatchId != s.MatchId && !used.Contains(m.MatchId))
                    .OrderBy(m => m.PlayedAt).Take(s.Matches).ToList();
                foreach (var m in next) used.Add(m.MatchId);
                result.Add(new SuspensionStatus(s.Player, s.Club, s.Reason, s.Detail, s.Matches, next.Count, s.At, s.MatchId));
            }
        }
        return new DisciplineReport(result.OrderByDescending(s => s.TriggeredAt).ToList(), pending);
    }
}
