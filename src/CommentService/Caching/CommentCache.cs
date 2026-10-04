using System.Text.Json;
using CommentService.Data;
using StackExchange.Redis;

namespace CommentService.Caching;

/// <summary>
/// CommentCache: holds the approved comments of recently read articles. It is filled on a cache miss
/// (cache-aside), and Redis evicts the least recently used articles when it reaches its memory limit.
/// Writes invalidate the article's entry. Redis errors count as a miss so requests fall back to the database.
/// </summary>
public sealed class CommentCache(IConnectionMultiplexer redis, IConfiguration configuration, ILogger<CommentCache> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TimeSpan _ttl = configuration.GetValue("CommentCache:Ttl", TimeSpan.FromMinutes(30));

    private IDatabase Db => redis.GetDatabase();

    private static RedisKey Key(Guid articleId) => $"comments:{articleId}";

    public async Task<List<Comment>?> GetAsync(Guid articleId)
    {
        try
        {
            var value = await Db.StringGetAsync(Key(articleId));
            return value.IsNull ? null : JsonSerializer.Deserialize<List<Comment>>(value.ToString(), Json);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("CommentCache unavailable ({Reason}); reading comments for {ArticleId} from database",
                ex.GetType().Name, articleId);
            return null;
        }
    }

    public async Task SetAsync(Guid articleId, List<Comment> comments)
    {
        try
        {
            await Db.StringSetAsync(Key(articleId), JsonSerializer.Serialize(comments, Json), _ttl);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("Could not cache comments for {ArticleId} ({Reason})", articleId, ex.GetType().Name);
        }
    }

    public async Task InvalidateAsync(params IEnumerable<Guid> articleIds)
    {
        var keys = articleIds.Distinct().Select(Key).ToArray();
        if (keys.Length == 0) return;

        try
        {
            await Db.KeyDeleteAsync(keys);
        }
        catch (RedisException ex)
        {
            logger.LogWarning("Could not invalidate cached comments for {ArticleIds} ({Reason})",
                keys.Select(k => k.ToString()), ex.GetType().Name);
        }
    }
}
