using SubscriberService;

namespace HappyHeadlines.Tests;

public class SubscriberRulesTests
{
    [Theory]
    [InlineData("anna@example.com", "anna@example.com")]
    [InlineData("  Lars@Example.COM ", "lars@example.com")]
    public void Accepts_plain_addresses_and_normalizes_them(string input, string expected)
    {
        Assert.True(SubscriberRules.TryNormalizeEmail(input, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("anna")]
    [InlineData("anna@")]
    [InlineData("anna@localhost")]
    [InlineData("Anna <anna@example.com>")]
    [InlineData("anna@example.com, lars@example.com")]
    public void Rejects_anything_that_is_not_a_single_plain_address(string? input)
    {
        Assert.False(SubscriberRules.TryNormalizeEmail(input, out var email));
        Assert.Equal("", email);
    }

    [Fact]
    public void Rejects_addresses_longer_than_the_database_column()
    {
        var tooLong = new string('a', SubscriberRules.MaxEmailLength) + "@example.com";

        Assert.False(SubscriberRules.TryNormalizeEmail(tooLong, out _));
    }
}
