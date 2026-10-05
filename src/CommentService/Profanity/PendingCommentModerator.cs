using CommentService.Caching;
using CommentService.Data;
using Microsoft.EntityFrameworkCore;

namespace CommentService.Profanity;

/// <summary>
/// Comments posted while ProfanityService was unavailable are stored as pending.
/// This job filters and approves them once the service (and the circuit) is back.
/// A failing tick (e.g. the database restarting) is logged and retried on the next tick,
/// because an unhandled exception in a BackgroundService stops the whole host.
/// </summary>
public sealed class PendingCommentModerator(
    IServiceScopeFactory scopeFactory, CommentCache cache, ILogger<PendingCommentModerator> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ModerateAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Moderating pending comments failed ({Reason}); retrying on next tick",
                    ex.InnerException?.Message ?? ex.Message);
            }
        }
    }

    private async Task ModerateAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommentDbContext>();
        var profanity = scope.ServiceProvider.GetRequiredService<ProfanityClient>();

        var pending = await db.Comments
            .Where(c => c.Status == CommentStatus.PendingModeration)
            .OrderBy(c => c.CreatedAt)
            .Take(50)
            .ToListAsync(stoppingToken);
        if (pending.Count == 0) return;

        var approved = 0;
        foreach (var comment in pending)
        {
            try
            {
                comment.Content = await profanity.FilterAsync(comment.Content, stoppingToken);
                comment.Status = CommentStatus.Approved;
                approved++;
            }
            catch (Exception ex) when (ProfanityClient.IsUnavailable(ex))
            {
                break;
            }
        }

        if (approved == 0) return;
        await db.SaveChangesAsync(stoppingToken);
        await cache.InvalidateAsync(pending.Where(c => c.Status == CommentStatus.Approved).Select(c => c.ArticleId));
        logger.LogInformation("Approved {Approved} of {Pending} pending comment(s)", approved, pending.Count);
    }
}
