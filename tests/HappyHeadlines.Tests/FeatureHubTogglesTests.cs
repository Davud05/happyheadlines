using HappyHeadlines.Shared.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HappyHeadlines.Tests;

public class FeatureHubTogglesTests
{
    private static FeatureHubToggles Create(Dictionary<string, string?> settings) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger<FeatureHubToggles>.Instance);

    [Fact]
    public void Toggles_are_off_when_FeatureHub_is_not_configured()
    {
        var toggles = Create([]);

        Assert.False(toggles.IsEnabled(FeatureFlags.SubscriberService));
    }

    [Fact]
    public void Toggles_are_off_with_a_malformed_api_key()
    {
        var toggles = Create(new()
        {
            ["FeatureHub:EdgeUrl"] = "http://featurehub:8085",
            ["FeatureHub:ApiKey"] = "not-a-real-key",
        });

        Assert.False(toggles.IsEnabled(FeatureFlags.SubscriberService));
    }

    [Fact]
    public async Task Toggles_are_off_until_FeatureHub_has_answered()
    {
        var toggles = Create(new()
        {
            ["FeatureHub:EdgeUrl"] = "http://127.0.0.1:9",
            ["FeatureHub:ApiKey"] = "default/00000000-0000-0000-0000-000000000000/abcdefghijklmnopqrstuvwxyz",
        });

        await toggles.StartAsync(CancellationToken.None);

        Assert.False(toggles.IsEnabled(FeatureFlags.SubscriberService));
    }
}
