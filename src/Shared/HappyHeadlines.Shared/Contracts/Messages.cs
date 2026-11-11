namespace HappyHeadlines.Shared.Contracts;

public static class Exchanges
{
    /// <summary>ArticleQueue: published articles fanned out to every subscribing service.</summary>
    public const string Articles = "articles";

    /// <summary>SubscriberQueue: subscribe and unsubscribe events from SubscriberService.</summary>
    public const string Subscribers = "subscribers";
}

public record ArticlePublished(
    Guid Id,
    string Title,
    string Content,
    string Author,
    string Continent,
    DateTimeOffset PublishedAt);

/// <summary>
/// One event type for both subscribing and unsubscribing, so both travel through the same queue in order.
/// <paramref name="OccurredAt"/> lets consumers ignore events that arrive after a newer one.
/// </summary>
public record SubscriptionChanged(
    Guid SubscriberId,
    string Email,
    bool Subscribed,
    DateTimeOffset OccurredAt);
