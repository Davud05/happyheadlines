using CommentService.Data;
using Microsoft.EntityFrameworkCore;

namespace CommentService.Profanity;

/// <summary>
/// Comments posted while ProfanityService was unavailable are stored as pending.
/// This job filters and approves them once the service (and the circuit) is back.
/// </summary>
public sealed class PendingCommentModerator(IServiceScopeFactory scopeFactory, ILogger<PendingCommentModerator> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CommentDbContext>();
            var profanity = scope.ServiceProvider.GetRequiredService<ProfanityClient>();

            var pending = await db.Comments
                .Where(c => c.Status == CommentStatus.PendingModeration)
                .OrderBy(c => c.CreatedAt)
                .Take(50)
                .ToListAsync(stoppingToken);
            if (pending.Count == 0) continue;

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

            if (approved == 0) continue;
            await db.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Approved {Approved} of {Pending} pending comment(s)", approved, pending.Count);
        }
    }
}
