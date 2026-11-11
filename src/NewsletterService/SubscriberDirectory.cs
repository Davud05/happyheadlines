using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Features;
using Microsoft.EntityFrameworkCore;
using NewsletterService.Data;

namespace NewsletterService;

/// <summary>
/// Decides who receives newsletters. With SUBSCRIBER_SERVICE on, that is the local copy of SubscriberService's
/// subscribers; with it off, the configured legacy list. SubscriberService is never called directly, so
/// newsletters keep going out while it (or its database) is down.
/// </summary>
public sealed class SubscriberDirectory(
    NewsletterDbContext db,
    IFeatureToggles toggles,
    IConfiguration configuration,
    ILogger<SubscriberDirectory> logger)
{
    public async Task<string[]> GetRecipientsAsync(CancellationToken ct)
    {
        if (!toggles.IsEnabled(FeatureFlags.SubscriberService))
        {
            logger.LogInformation("{Feature} is off, using the configured subscriber list", FeatureFlags.SubscriberService);
            return configuration.GetSection("Newsletter:Subscribers").Get<string[]>() ?? [];
        }

        return await db.Subscribers.AsNoTracking()
            .Where(s => s.Active)
            .OrderBy(s => s.Email)
            .Select(s => s.Email)
            .ToArrayAsync(ct);
    }

    /// <summary>Idempotent and order-tolerant: replays and events older than the stored state are ignored.</summary>
    public async Task ApplyAsync(SubscriptionChanged change, CancellationToken ct)
    {
        var email = change.Email.Trim().ToLowerInvariant();
        var existing = await db.Subscribers.FindAsync([email], ct);

        if (existing is null)
        {
            db.Subscribers.Add(new NewsletterSubscriber
            {
                Email = email,
                SubscriberId = change.SubscriberId,
                Active = change.Subscribed,
                ChangedAt = change.OccurredAt,
            });
        }
        else if (change.OccurredAt > existing.ChangedAt)
        {
            existing.SubscriberId = change.SubscriberId;
            existing.Active = change.Subscribed;
            existing.ChangedAt = change.OccurredAt;
        }
        else
        {
            logger.LogInformation("Ignored outdated subscription event for subscriber {SubscriberId}", change.SubscriberId);
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Subscriber {SubscriberId} is now {State}", change.SubscriberId,
            change.Subscribed ? "subscribed" : "unsubscribed");
    }
}
