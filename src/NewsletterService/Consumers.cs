using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Messaging;
using Npgsql;

namespace NewsletterService;

public sealed class BreakingNewsConsumer(RabbitMqConnection connection, IServiceScopeFactory scopes, ILogger<BreakingNewsConsumer> logger)
    : MessageConsumer<ArticlePublished>(connection, scopes, logger, Exchanges.Articles, "newsletter-service.articles")
{
    protected override Task HandleAsync(ArticlePublished message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<Newsletter>().SendBreakingNewsAsync(message, ct);
}

public sealed class SubscriptionConsumer(RabbitMqConnection connection, IServiceScopeFactory scopes, ILogger<SubscriptionConsumer> logger)
    : MessageConsumer<SubscriptionChanged>(connection, scopes, logger, Exchanges.Subscribers, "newsletter-service.subscribers")
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// While the newsletter database is down the event stays unacknowledged in SubscriberQueue and is retried,
    /// instead of being dropped after one requeue like other messages.
    /// </summary>
    protected override async Task HandleAsync(SubscriptionChanged message, IServiceProvider services, CancellationToken ct)
    {
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        while (true)
        {
            try
            {
                // A fresh scope per attempt, so a failed attempt's tracked changes cannot leak into the next.
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SubscriberDirectory>().ApplyAsync(message, ct);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Newsletter database unavailable, retrying subscriber {SubscriberId} in 5s: {Reason}",
                    message.SubscriberId, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is NpgsqlException { IsTransient: true } || ex.InnerException is NpgsqlException { IsTransient: true };
}

public sealed class DailyNewsletterJob(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<DailyNewsletterJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sendAt = TimeOnly.TryParse(configuration["Newsletter:DailyAtUtc"], out var time) ? time : new TimeOnly(6, 0);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = now.Date + sendAt.ToTimeSpan();
            if (next <= now) next = next.AddDays(1);

            logger.LogInformation("Next daily newsletter scheduled for {NextRun:u}", next);
            await Task.Delay(next - now, stoppingToken);

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Newsletter>().SendDailyAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Daily newsletter failed");
            }
        }
    }
}
