using FeatureHubSDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HappyHeadlines.Shared.Features;

public static class FeatureFlags
{
    /// <summary>Release toggle for SubscriberService and everything built on top of it.</summary>
    public const string SubscriberService = "SUBSCRIBER_SERVICE";

    public static readonly IReadOnlyList<string> All = [SubscriberService];
}

public interface IFeatureToggles
{
    bool IsEnabled(string feature);
}

/// <summary>
/// Release toggles served by FeatureHub over a streaming connection, so flipping a toggle in the FeatureHub
/// admin console takes effect within seconds without restarting or redeploying anything.
/// A toggle is off until FeatureHub has answered at least once; after that the last received value is kept
/// while FeatureHub is unreachable.
/// </summary>
public sealed class FeatureHubToggles : IFeatureToggles, IHostedService
{
    private readonly ILogger<FeatureHubToggles> _logger;
    private readonly IFeatureHubConfig? _config;

    public FeatureHubToggles(IConfiguration configuration, ILogger<FeatureHubToggles> logger)
    {
        _logger = logger;
        var edgeUrl = configuration["FeatureHub:EdgeUrl"];
        var apiKey = configuration["FeatureHub:ApiKey"];
        if (string.IsNullOrWhiteSpace(edgeUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("FeatureHub is not configured (FeatureHub:EdgeUrl / FeatureHub:ApiKey); all feature toggles are off");
            return;
        }

        try
        {
            _config = new EdgeFeatureHubConfig(edgeUrl, apiKey);
        }
        catch (Exception ex)
        {
            logger.LogError("FeatureHub configuration rejected ({Reason}); all feature toggles are off", ex.Message);
        }
    }

    public bool IsEnabled(string feature)
    {
        if (_config is null) return false;
        try
        {
            return _config.Repository.IsEnabled(feature);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Reading feature toggle {Feature} failed ({Reason}); treating it as off", feature, ex.Message);
            return false;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_config is null) return Task.CompletedTask;

        FeatureLogging.ErrorLogger += (_, message) => _logger.LogWarning("FeatureHub: {Message}", message);
        foreach (var key in FeatureFlags.All)
        {
            _config.Repository.GetFeature(key).FeatureUpdateHandler += (_, feature) =>
                _logger.LogInformation("Feature toggle {Feature} is now {State}", feature.Key, feature.IsEnabled ? "ON" : "OFF");
        }

        // Not awaited: the service must start (with toggles off) even when FeatureHub is down.
        _ = Task.Run(async () =>
        {
            try
            {
                await _config.Init();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Connecting to FeatureHub failed ({Reason}); toggles stay off until it answers", ex.Message);
            }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class FeatureToggleExtensions
{
    public static IServiceCollection AddFeatureToggles(this IServiceCollection services)
    {
        services.AddSingleton<FeatureHubToggles>();
        services.AddSingleton<IFeatureToggles>(sp => sp.GetRequiredService<FeatureHubToggles>());
        services.AddHostedService(sp => sp.GetRequiredService<FeatureHubToggles>());
        return services;
    }
}
