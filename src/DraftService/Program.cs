using System.Diagnostics;
using DraftService.Data;
using HappyHeadlines.Shared;
using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Observability;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("DraftService");
builder.Services.AddDbContext<DraftDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Drafts")));

var app = builder.Build();
app.UseObservability();

await StartupRetry.RunAsync(async () =>
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<DraftDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Drafts.AnyAsync())
    {
        db.Drafts.AddRange(SeedDrafts.Create());
        await db.SaveChangesAsync();
    }
}, app.Logger, "Creating draft schema");

var tracer = new ActivitySource("DraftService");
var drafts = app.MapGroup("/api/drafts");

drafts.MapGet("/", async (DraftDbContext db, string? author, ILogger<Program> logger) =>
{
    var query = db.Drafts.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(author)) query = query.Where(d => d.Author == author);

    var result = await query.OrderByDescending(d => d.UpdatedAt).ToListAsync();
    logger.LogDebug("Listed {Count} drafts for author {Author}", result.Count, author ?? "<all>");
    return result;
});

drafts.MapGet("/{id:guid}", async (Guid id, DraftDbContext db) =>
{
    Activity.Current?.SetTag("draft.id", id);
    return await db.Drafts.FindAsync(id) is { } draft ? Results.Ok(draft) : Results.NotFound();
});

drafts.MapPost("/", async (DraftRequest request, DraftDbContext db, ILogger<Program> logger) =>
{
    if (request.Validate() is { } errors)
    {
        logger.LogWarning("Rejected draft from {Author}: {@Errors}", request.Author, errors);
        return Results.ValidationProblem(errors);
    }

    var now = DateTimeOffset.UtcNow;
    var draft = new Draft
    {
        Id = Guid.NewGuid(),
        Title = request.Title,
        Content = request.Content ?? "",
        Author = request.Author,
        Continent = request.NormalizedContinent,
        CreatedAt = now,
        UpdatedAt = now,
    };

    using (var activity = tracer.StartActivity("Save new draft"))
    {
        activity?.SetTag("draft.id", draft.Id);
        activity?.SetTag("draft.author", draft.Author);
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();
    }

    logger.LogInformation("Draft {DraftId} created by {Author}", draft.Id, draft.Author);
    return Results.Created($"/api/drafts/{draft.Id}", draft);
});

drafts.MapPut("/{id:guid}", async (Guid id, DraftRequest request, DraftDbContext db, ILogger<Program> logger) =>
{
    Activity.Current?.SetTag("draft.id", id);
    if (request.Validate() is { } errors) return Results.ValidationProblem(errors);

    var draft = await db.Drafts.FindAsync(id);
    if (draft is null)
    {
        logger.LogWarning("Update of unknown draft {DraftId}", id);
        return Results.NotFound();
    }

    draft.Title = request.Title;
    draft.Content = request.Content ?? "";
    draft.Author = request.Author;
    draft.Continent = request.NormalizedContinent;
    draft.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    logger.LogInformation("Draft {DraftId} updated by {Author}", id, draft.Author);
    return Results.Ok(draft);
});

drafts.MapDelete("/{id:guid}", async (Guid id, DraftDbContext db, ILogger<Program> logger) =>
{
    Activity.Current?.SetTag("draft.id", id);
    if (await db.Drafts.Where(d => d.Id == id).ExecuteDeleteAsync() == 0)
    {
        logger.LogWarning("Delete of unknown draft {DraftId}", id);
        return Results.NotFound();
    }

    logger.LogInformation("Draft {DraftId} deleted", id);
    return Results.NoContent();
});

app.Run();

record DraftRequest(string Title, string? Content, string Author, string? Continent)
{
    public string NormalizedContinent =>
        Continents.TryNormalize(Continent, out var continent) ? continent : Continents.Global;

    public Dictionary<string, string[]>? Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Title)) errors[nameof(Title)] = ["Title is required."];
        if (string.IsNullOrWhiteSpace(Author)) errors[nameof(Author)] = ["Author is required."];
        return errors.Count > 0 ? errors : null;
    }
}
