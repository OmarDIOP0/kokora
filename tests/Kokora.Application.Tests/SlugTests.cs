using FluentAssertions;
using Kokora.Application.Common;

namespace Kokora.Application.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("ASC Jeanne d'Arc", "asc-jeanne-d-arc")]
    [InlineData("  Zonale 5A  ", "zonale-5a")]
    [InlineData("Coupe du Maire — édition 2026", "coupe-du-maire-edition-2026")]
    [InlineData("Nguékokh", "nguekokh")]
    [InlineData("!!!", "")]
    public void From_removes_accents_and_punctuation(string input, string expected) =>
        Slug.From(input).Should().Be(expected);

    [Fact]
    public async Task UniqueAsync_appends_a_counter()
    {
        var taken = new HashSet<string> { "demo", "demo-2" };
        var slug = await Slug.UniqueAsync("Démo", s => Task.FromResult(taken.Contains(s)));
        slug.Should().Be("demo-3");
    }
}
