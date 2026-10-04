using System.Text.Json;
using ArticleService.Data;
using StackExchange.Redis;

namespace ArticleService.Caching;

/// <summary>
/// ArticleCache: an offline cache holding every article published within the last 14 days.
/// It is filled ahead of time by <see cref="ArticleCacheLoader"/>, never on a cache miss.
/// Per continent it keeps one JSON entry per article and a sorted set of ids scored by publish time.
/// Redis errors are logged and reported as a miss so requests fall back to the database.
/// </summary>
public sealed class ArticleCache(IConnectionMultiplexer redis, ILogger<ArticleCache> logger)
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string HasOlder = "has-older";
    private const string Complete = "complete";

    private IDatabase Db => redis.GetDatabase();

    private static RedisKey IndexKey(string continent) => $"articles:{continent}";
    private static RedisKey LoadedKey(string continent) => $"articles:{continent}:loaded";
    private static RedisKey ArticleKey(string continent, Guid id) => $"article:{continent}:{id}";

    public static DateTimeOffset WindowStart => DateTimeOffset.UtcNow - Window;

    public static bool InWindow(DateTimeOffset publishedAt) => publishedAt >= WindowStart;

    /// <summary>
    /// Newest articles published at or after <paramref name="since"/> (defaults to the window start).
    /// Returns null when the cache cannot answer completely and the database must be asked.
    /// </summary>
    public async Task<List<Article>?> GetRecentAsync(string continent, DateTimeOffset? since, int limit)
    {
        if (since is { } requested && !InWindow(requested)) return null;
        var from = since ?? WindowStart;

        try
        {
            var loaded = await Db.StringGetAsync(LoadedKey(continent));
            if (loaded.IsNull) return null;

            var ids = await Db.SortedSetRangeByScoreAsync(IndexKey(continent),
                start: from.ToUnixTimeMilliseconds(), order: Order.Descending, take: limit);

            // Fewer recent articles than requested: the rest must come from older articles in the database.
            if (since is null && ids.Length < limit && loaded == HasOlder) return null;

            var values = await Db.StringGetAsync(ids.Select(id => ArticleKey(continent, Guid.Parse(id.ToString()))).ToArray());
            if (values.Any(v => v.IsNull)) return null;

            return values.Select(v => JsonSerializer.Deserialize<Article>(v.ToString(), Json)!).ToList();
        }
        catch (RedisException ex)
        {
            logger.LogWarning("ArticleCache unavailable ({Reason}); reading recent {Continent} articles from database",
                ex.GetType().Name, continent);
            return null;
        }
    }

    public async Task<Article?> GetAsync(string continent, Guid id)
    {
        try
        {
            var value = await Db.StringGetAsync(ArticleKey(continent, id));
            return value.IsNull ? null : JsonSerializer.Deserialize<Article>(value.ToString(), Json);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("ArticleCache unavailable ({Reason}); reading article {ArticleId} from database",
                ex.GetType().Name, id);
            return null;
        }
    }

    public async Task SetAsync(Article article)
    {
        if (!InWindow(article.PublishedAt))
        {
            await RemoveAsync(article.Continent, article.Id);
            return;
        }

        try
        {
            await Task.WhenAll(Write(Db, article));
        }
        catch (RedisException ex)
        {
            logger.LogWarning("Could not cache article {ArticleId} ({Reason})", article.Id, ex.GetType().Name);
        }
    }

    public async Task RemoveAsync(string continent, Guid id)
    {
        try
        {
            await Task.WhenAll(
                Db.KeyDeleteAsync(ArticleKey(continent, id)),
                Db.SortedSetRemoveAsync(IndexKey(continent), id.ToString()));
        }
        catch (RedisException ex)
        {
            logger.LogWarning("Could not remove article {ArticleId} from cache ({Reason})", id, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Replaces the cached window for a continent with <paramref name="articles"/> in one transaction.
    /// <paramref name="hasOlder"/> tells whether the database also holds articles older than the window.
    /// </summary>
    public async Task ReplaceWindowAsync(string continent, IReadOnlyCollection<Article> articles, bool hasOlder)
    {
        var index = IndexKey(continent);
        var cachedIds = await Db.SortedSetRangeByRankAsync(index);
        var currentIds = articles.Select(a => a.Id.ToString()).ToHashSet();
        var staleIds = cachedIds.Select(id => id.ToString()).Where(id => !currentIds.Contains(id)).ToList();

        var tx = Db.CreateTransaction();
        var writes = new List<Task>();
        foreach (var id in staleIds)
        {
            writes.Add(tx.KeyDeleteAsync(ArticleKey(continent, Guid.Parse(id))));
            writes.Add(tx.SortedSetRemoveAsync(index, id));
        }
        foreach (var article in articles) writes.AddRange(Write(tx, article));
        writes.Add(tx.StringSetAsync(LoadedKey(continent), hasOlder ? HasOlder : Complete));

        await tx.ExecuteAsync();
        await Task.WhenAll(writes);
    }

    private static IEnumerable<Task> Write(IDatabaseAsync db, Article article)
    {
        var expiresIn = article.PublishedAt + Window - DateTimeOffset.UtcNow;
        if (expiresIn < TimeSpan.FromSeconds(1)) expiresIn = TimeSpan.FromSeconds(1);
        yield return db.StringSetAsync(ArticleKey(article.Continent, article.Id),
            JsonSerializer.Serialize(article, Json), expiresIn);
        yield return db.SortedSetAddAsync(IndexKey(article.Continent), article.Id.ToString(),
            article.PublishedAt.ToUnixTimeMilliseconds());
        yield return db.SortedSetRemoveRangeByScoreAsync(IndexKey(article.Continent),
            double.NegativeInfinity, WindowStart.ToUnixTimeMilliseconds(), Exclude.Stop);
    }
}
