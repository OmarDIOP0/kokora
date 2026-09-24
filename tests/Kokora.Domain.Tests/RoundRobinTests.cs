using FluentAssertions;
using Kokora.Domain.Rules;

namespace Kokora.Domain.Tests;

public class RoundRobinTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Every_pair_meets_exactly_once_in_single_leg(int count)
    {
        var teams = Enumerable.Range(1, count).ToList();
        var fixtures = RoundRobin.Generate(teams, homeAndAway: false);

        fixtures.Should().HaveCount(count * (count - 1) / 2);
        fixtures.Select(f => (Math.Min(f.HomeId, f.AwayId), Math.Max(f.HomeId, f.AwayId)))
            .Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Each_team_plays_at_most_once_per_matchday(int count)
    {
        var fixtures = RoundRobin.Generate(Enumerable.Range(1, count).ToList(), homeAndAway: true);
        foreach (var day in fixtures.GroupBy(f => f.Matchday))
            day.SelectMany(f => new[] { f.HomeId, f.AwayId }).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Odd_count_gives_one_bye_per_matchday()
    {
        var fixtures = RoundRobin.Generate([1, 2, 3, 4, 5], homeAndAway: false);
        fixtures.Select(f => f.Matchday).Distinct().Should().HaveCount(5);
        fixtures.GroupBy(f => f.Matchday).Should().OnlyContain(g => g.Count() == 2);
    }

    [Fact]
    public void Home_and_away_mirrors_first_leg()
    {
        var fixtures = RoundRobin.Generate([1, 2, 3, 4], homeAndAway: true);
        fixtures.Should().HaveCount(12);
        fixtures.Select(f => (f.HomeId, f.AwayId)).Should().OnlyHaveUniqueItems();
        fixtures.Max(f => f.Matchday).Should().Be(6);
    }

    [Fact]
    public void Home_games_are_balanced()
    {
        var fixtures = RoundRobin.Generate(Enumerable.Range(1, 6).ToList(), homeAndAway: false);
        var homeCounts = fixtures.GroupBy(f => f.HomeId).Select(g => g.Count()).ToList();
        (homeCounts.Max() - homeCounts.DefaultIfEmpty(0).Min()).Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Duplicate_team_is_rejected()
    {
        var act = () => RoundRobin.Generate([1, 2, 2], homeAndAway: false);
        act.Should().Throw<ArgumentException>();
    }
}
