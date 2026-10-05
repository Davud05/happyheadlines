namespace HappyHeadlines.Shared.Contracts;

public static class Exchanges
{
    /// <summary>ArticleQueue: published articles fanned out to every subscribing service.</summary>
    public const string Articles = "articles";
}

public record ArticlePublished(
    Guid Id,
    string Title,
    string Content,
    string Author,
    string Continent,
    DateTimeOffset PublishedAt);
