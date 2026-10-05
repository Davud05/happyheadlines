using ArticleService.Caching;
using ArticleService.Data;
using ArticleService.Messaging;
using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Messaging;
using HappyHeadlines.Shared.Observability;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("ArticleService");
builder.Services.AddSingleton<ArticleDatabases>();
builder.Services.AddMessaging();
builder.Services.AddHostedService<ArticleQueueConsumer>();
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(builder.Configuration.GetConnectionString("ArticleCache") ?? "localhost:6379");
    options.AbortOnConnectFail = false;
    options.BacklogPolicy = BacklogPolicy.FailFast;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<ArticleCache>();
builder.Services.AddHostedService<ArticleCacheLoader>();

var app = builder.Build();
app.UseObservability();

var instanceId = app.Configuration["INSTANCE_ID"] ?? Environment.MachineName;
app.Use((ctx, next) =>
{
    ctx.Response.Headers["X-Served-By"] = instanceId;
    return next();
});

await app.Services.GetRequiredService<ArticleDatabases>().EnsureCreatedAsync(app.Logger);

var articles = app.MapGroup("/api/articles");

articles.MapGet("/", async (ArticleDatabases dbs, ArticleCache cache, HttpResponse response,
    string? continent, int? limit, DateTimeOffset? since) =>
{
    if (!TryContinent(continent, out var region)) return InvalidContinent(continent);
    var take = Math.Clamp(limit ?? 10, 1, 100);

    var cached = await cache.GetRecentAsync(region, since, take);
    CacheMetrics.Record(response, "ArticleCache", "list", hit: cached is not null);
    if (cached is not null) return Results.Ok(cached);

    await using var db = dbs.Open(region);
    var query = db.Articles.AsNoTracking();
    if (since is { } from)
    {
        var fromUtc = from.ToUniversalTime();
        query = query.Where(a => a.PublishedAt >= fromUtc);
    }

    var result = await query
        .OrderByDescending(a => a.PublishedAt)
        .Take(take)
        .ToListAsync();
    return Results.Ok(result);
});

articles.MapGet("/{id:guid}", async (ArticleDatabases dbs, ArticleCache cache, HttpResponse response,
    Guid id, string? continent) =>
{
    if (!TryContinent(continent, out var region)) return InvalidContinent(continent);

    var cached = await cache.GetAsync(region, id);
    CacheMetrics.Record(response, "ArticleCache", "get", hit: cached is not null);
    if (cached is not null) return Results.Ok(cached);

    await using var db = dbs.Open(region);
    var article = await db.Articles.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    return article is null ? Results.NotFound() : Results.Ok(article);
});

articles.MapPost("/", async (ArticleDatabases dbs, ArticleCache cache, ArticleRequest request, ILogger<Program> logger) =>
{
    if (!TryContinent(request.Continent, out var region)) return InvalidContinent(request.Continent);
    if (request.Validate() is { } errors) return Results.ValidationProblem(errors);

    var article = new Article
    {
        Id = Guid.NewGuid(),
        Title = request.Title,
        Content = request.Content,
        Author = request.Author,
        Continent = region,
        PublishedAt = DateTimeOffset.UtcNow,
    };

    await using var db = dbs.Open(region);
    db.Articles.Add(article);
    await db.SaveChangesAsync();
    await cache.SetAsync(article);

    logger.LogInformation("Created article {ArticleId} in {Continent}", article.Id, region);
    return Results.Created($"/api/articles/{article.Id}?continent={region}", article);
});

articles.MapPut("/{id:guid}", async (ArticleDatabases dbs, ArticleCache cache, Guid id, string? continent,
    ArticleRequest request, ILogger<Program> logger) =>
{
    if (!TryContinent(continent, out var region)) return InvalidContinent(continent);
    if (request.Validate() is { } errors) return Results.ValidationProblem(errors);

    await using var db = dbs.Open(region);
    var article = await db.Articles.FindAsync(id);
    if (article is null) return Results.NotFound();

    article.Title = request.Title;
    article.Content = request.Content;
    article.Author = request.Author;
    article.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    await cache.SetAsync(article);

    logger.LogInformation("Updated article {ArticleId} in {Continent}", id, region);
    return Results.Ok(article);
});

articles.MapDelete("/{id:guid}", async (ArticleDatabases dbs, ArticleCache cache, Guid id, string? continent,
    ILogger<Program> logger) =>
{
    if (!TryContinent(continent, out var region)) return InvalidContinent(continent);

    await using var db = dbs.Open(region);
    var deleted = await db.Articles.Where(a => a.Id == id).ExecuteDeleteAsync();
    if (deleted == 0) return Results.NotFound();
    await cache.RemoveAsync(region, id);

    logger.LogInformation("Deleted article {ArticleId} from {Continent}", id, region);
    return Results.NoContent();
});

app.Run();

static bool TryContinent(string? value, out string continent)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        continent = Continents.Global;
        return true;
    }
    return Continents.TryNormalize(value, out continent);
}

static IResult InvalidContinent(string? value) =>
    Results.BadRequest(new { error = $"Unknown continent '{value}'.", valid = Continents.All });

record ArticleRequest(string Title, string Content, string Author, string? Continent)
{
    public Dictionary<string, string[]>? Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Title)) errors[nameof(Title)] = ["Title is required."];
        if (string.IsNullOrWhiteSpace(Content)) errors[nameof(Content)] = ["Content is required."];
        if (string.IsNullOrWhiteSpace(Author)) errors[nameof(Author)] = ["Author is required."];
        return errors.Count > 0 ? errors : null;
    }
}
