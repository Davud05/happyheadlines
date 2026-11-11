using HappyHeadlines.Shared.Contracts;

namespace HappyHeadlines.Tests;

public class ContinentsTests
{
    [Theory]
    [InlineData("europe", "Europe")]
    [InlineData("NORTHAMERICA", "NorthAmerica")]
    [InlineData("Global", "Global")]
    public void Normalizes_known_continents_to_their_canonical_name(string input, string expected)
    {
        Assert.True(Continents.TryNormalize(input, out var continent));
        Assert.Equal(expected, continent);
    }

    [Theory]
    [InlineData("Atlantis")]
    [InlineData("North America")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_unknown_continents(string? input)
    {
        Assert.False(Continents.TryNormalize(input, out var continent));
        Assert.Equal("", continent);
    }

    [Fact]
    public void Has_a_database_shard_for_every_continent_plus_global()
    {
        Assert.Equal(8, Continents.All.Count);
        Assert.Contains(Continents.Global, Continents.All);
        Assert.Equal(Continents.All.Count, Continents.All.Distinct().Count());
    }
}
