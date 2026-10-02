using System.Text.Json.Serialization;
using CommentService.Data;
using CommentService.Profanity;
using HappyHeadlines.Shared;
using HappyHeadlines.Shared.Observability;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("CommentService");
builder.Services.AddDbContext<CommentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Comments")));
builder.Services.AddProfanityClient(builder.Configuration);
builder.Services.AddHostedService<PendingCommentModerator>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseObservability();

await StartupRetry.RunAsync(async () =>
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CommentDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Comments.AnyAsync())
    {
        db.Comments.AddRange(SeedComments.Create());
        await db.SaveChangesAsync();
    }
}, app.Logger, "Creating comment schema");

var comments = app.MapGroup("/api/comments");

comments.MapGet("/", async (Guid articleId, CommentDbContext db) =>
    await db.Comments.AsNoTracking()
        .Where(c => c.ArticleId == articleId && c.Status == CommentStatus.Approved)
        .OrderBy(c => c.CreatedAt)
        .ToListAsync());

comments.MapGet("/{id:guid}", async (Guid id, CommentDbContext db) =>
    await db.Comments.FindAsync(id) is { } comment ? Results.Ok(comment) : Results.NotFound());

comments.MapPost("/", async (CommentRequest request, CommentDbContext db, ProfanityClient profanity,
    ILogger<Program> logger, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Author) || string.IsNullOrWhiteSpace(request.Content))
        return Results.BadRequest(new { error = "Author and content are required." });

    var comment = new Comment
    {
        Id = Guid.NewGuid(),
        ArticleId = request.ArticleId,
        Author = request.Author.Trim(),
        Content = request.Content.Trim(),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    try
    {
        comment.Content = await profanity.FilterAsync(comment.Content, ct);
        comment.Status = CommentStatus.Approved;
    }
    catch (Exception ex) when (ProfanityClient.IsUnavailable(ex))
    {
        // Fallback: never publish unfiltered text. Hold the comment until it can be moderated.
        comment.Status = CommentStatus.PendingModeration;
        logger.LogWarning("ProfanityService unavailable ({Reason}); comment {CommentId} held for moderation",
            ex.GetType().Name, comment.Id);
    }

    db.Comments.Add(comment);
    await db.SaveChangesAsync(ct);
    logger.LogInformation("Stored comment {CommentId} on article {ArticleId} as {Status}",
        comment.Id, comment.ArticleId, comment.Status);

    return comment.Status == CommentStatus.Approved
        ? Results.Created($"/api/comments/{comment.Id}", comment)
        : Results.Accepted($"/api/comments/{comment.Id}", comment);
});

comments.MapDelete("/{id:guid}", async (Guid id, CommentDbContext db) =>
    await db.Comments.Where(c => c.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound());

app.Run();

record CommentRequest(Guid ArticleId, string Author, string Content);
