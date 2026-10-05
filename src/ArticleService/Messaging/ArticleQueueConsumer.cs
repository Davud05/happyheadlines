using ArticleService.Caching;
using ArticleService.Data;
using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ArticleService.Messaging;

/// <summary>
/// Persists published articles from the ArticleQueue. All ArticleService instances share the
/// "article-service.articles" queue, so each article is stored exactly once.
/// </summary>
public sealed class ArticleQueueConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ArticleDatabases databases,
    ArticleCache cache,
    ILogger<ArticleQueueConsumer> logger)
    : MessageConsumer<ArticlePublished>(connection, scopeFactory, logger, Exchanges.Articles, "article-service.articles")
{
    protected override async Task HandleAsync(ArticlePublished message, IServiceProvider services, CancellationToken ct)
    {
        var continent = Continents.TryNormalize(message.Continent, out var c) ? c : Continents.Global;

        await using var db = databases.Open(continent);
        if (await db.Articles.AnyAsync(a => a.Id == message.Id, ct))
        {
            logger.LogInformation("Article {ArticleId} already stored, skipping duplicate delivery", message.Id);
            return;
        }

        var article = new Article
        {
            Id = message.Id,
            Title = message.Title,
            Content = message.Content,
            Author = message.Author,
            Continent = continent,
            PublishedAt = message.PublishedAt.ToUniversalTime(),
        };
        db.Articles.Add(article);
        await db.SaveChangesAsync(ct);
        await cache.SetAsync(article);

        logger.LogInformation("Stored published article {ArticleId} in {Continent}", message.Id, continent);
    }
}
