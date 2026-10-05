using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Messaging;

namespace NewsletterService;

public sealed class BreakingNewsConsumer(RabbitMqConnection connection, IServiceScopeFactory scopes, ILogger<BreakingNewsConsumer> logger)
    : MessageConsumer<ArticlePublished>(connection, scopes, logger, Exchanges.Articles, "newsletter-service.articles")
{
    protected override Task HandleAsync(ArticlePublished message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<Newsletter>().SendBreakingNewsAsync(message, ct);
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
