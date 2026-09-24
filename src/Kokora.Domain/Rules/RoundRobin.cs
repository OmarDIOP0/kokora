namespace Kokora.Domain.Rules;

public readonly record struct Fixture(int Matchday, int HomeId, int AwayId);

/// <summary>
/// Génère le calendrier d'une poule « chacun contre chacun » (tables de Berger).
/// Chaque équipe joue une fois par journée (exempte si nombre impair) et les réceptions sont alternées.
/// </summary>
public static class RoundRobin
{
    public static IReadOnlyList<Fixture> Generate(IReadOnlyList<int> teamIds, bool homeAndAway)
    {
        if (teamIds.Count < 2) return [];
        if (teamIds.Distinct().Count() != teamIds.Count)
            throw new ArgumentException("Une équipe apparaît deux fois dans la poule.", nameof(teamIds));

        // Exempt représenté par null quand le nombre d'équipes est impair.
        var slots = teamIds.Select(id => (int?)id).ToList();
        if (slots.Count % 2 == 1) slots.Add(null);
        var n = slots.Count;
        var rounds = n - 1;
        var fixtures = new List<Fixture>();

        for (var r = 0; r < rounds; r++)
        {
            for (var i = 0; i < n / 2; i++)
            {
                var a = slots[i];
                var b = slots[n - 1 - i];
                if (a is null || b is null) continue;
                // Inverser une journée sur deux équilibre les réceptions (écart max. de 1 à 2 matchs).
                var (home, away) = r % 2 == 1 ? (b.Value, a.Value) : (a.Value, b.Value);
                fixtures.Add(new Fixture(r + 1, home, away));
            }
            // Rotation : le premier reste fixe, le dernier passe en deuxième position.
            var last = slots[^1];
            slots.RemoveAt(n - 1);
            slots.Insert(1, last);
        }

        if (homeAndAway)
        {
            var firstLeg = fixtures.ToList();
            fixtures.AddRange(firstLeg.Select(f => new Fixture(f.Matchday + rounds, f.AwayId, f.HomeId)));
        }
        return fixtures;
    }
}
