using FluentAssertions;
using Kokora.Domain.Clubs;

namespace Kokora.Domain.Tests;

public class ClubInitialsTests
{
    [Theory]
    [InlineData("ASC Démo 1", "D1")]
    [InlineData("ASC Démo 12", "D12")]
    [InlineData("Guinaw Rails 2", "GR2")]
    [InlineData("ASC Jeanne d'Arc", "JA")]
    [InlineData("Espoir", "ESP")]
    [InlineData("ASC Union Sportive de la Médina", "USM")]
    [InlineData("ASC", "ASC")]
    public void From_ignores_common_words_and_keeps_three_letters_max(string name, string expected) =>
        ClubInitials.From(name).Should().Be(expected);

    [Fact]
    public void From_handles_empty_name() => ClubInitials.From("").Should().Be("?");
}
