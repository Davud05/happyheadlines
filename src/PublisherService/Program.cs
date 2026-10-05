using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Messaging;
using HappyHeadlines.Shared.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("PublisherService");
builder.Services.AddMessaging();
builder.Services.AddHttpClient("profanity", client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Profanity"] ?? "http://localhost:8002"))
    .AddStandardResilienceHandler();

var app = builder.Build();
app.UseObservability();

app.MapPost("/api/publish", async (PublishRequest request, IHttpClientFactory http, MessagePublisher publisher,
    ILogger<Program> logger, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.Author))
        return Results.BadRequest(new { error = "Title, content and author are required." });

    var continent = Continents.TryNormalize(request.Continent, out var c) ? c : Continents.Global;
    var profanity = http.CreateClient("profanity");

    string title, content;
    try
    {
        title = await FilterAsync(profanity, request.Title, ct);
        content = await FilterAsync(profanity, request.Content, ct);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Polly.ExecutionRejectedException)
    {
        logger.LogWarning("Publishing rejected, ProfanityService unavailable: {Reason}", ex.GetType().Name);
        return Results.Problem("Articles cannot be published while profanity filtering is unavailable.", statusCode: 503);
    }

    var article = new ArticlePublished(Guid.NewGuid(), title, content, request.Author.Trim(), continent, DateTimeOffset.UtcNow);
    await publisher.PublishAsync(Exchanges.Articles, article, ct);

    logger.LogInformation("Published article {ArticleId} to {Continent} (draft {DraftId})", article.Id, continent, request.DraftId);
    return Results.Accepted(value: new { articleId = article.Id, continent });
});

app.Run();

static async Task<string> FilterAsync(HttpClient client, string text, CancellationToken ct)
{
    using var response = await client.PostAsJsonAsync("/api/profanity/filter", new { text }, ct);
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<FilterResponse>(ct);
    return result?.Text ?? throw new HttpRequestException("ProfanityService returned an empty response.");
}

record PublishRequest(string Title, string Content, string Author, string? Continent, Guid? DraftId);

record FilterResponse(string Text);
