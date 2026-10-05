using ArticleService.Data;
using HappyHeadlines.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ArticleService.Caching;

/// <summary>
/// Fills the offline ArticleCache: on startup and then on a fixed interval it loads every article
/// from the last 14 days per continent and drops the ones that have fallen out of the window.
/// Every ArticleService instance runs it; the writes are idempotent.
/// </summary>
public sealed class ArticleCacheLoader(
    ArticleDatabases dbs, ArticleCache cache, IConfiguration configuration, ILogger<ArticleCacheLoader> logger)
    : BackgroundService
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshInterval = configuration.GetValue("ArticleCache:RefreshInterval", TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = await LoadAllAsync(stoppingToken);
            await Task.Delay(succeeded ? refreshInterval : RetryInterval, stoppingToken);
        }
    }

    private async Task<bool> LoadAllAsync(CancellationToken ct)
    {
        try
        {
            var total = 0;
            foreach (var continent in Continents.All)
            {
                var from = ArticleCache.WindowStart;
                await using var db = dbs.Open(continent);
                var articles = await db.Articles.AsNoTracking()
                    .Where(a => a.PublishedAt >= from)
                    .ToListAsync(ct);
                var hasOlder = await db.Articles.AnyAsync(a => a.PublishedAt < from, ct);

                await cache.ReplaceWindowAsync(continent, articles, hasOlder);
                total += articles.Count;
            }

            logger.LogInformation("ArticleCache loaded with {Count} article(s) from the last {Days} days",
                total, ArticleCache.Window.TotalDays);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Loading ArticleCache failed ({Reason}); retrying in {Retry}s",
                ex.InnerException?.Message ?? ex.Message, RetryInterval.TotalSeconds);
            return false;
        }
    }
}
