using HappyHeadlines.Shared;
using HappyHeadlines.Shared.Observability;
using Microsoft.EntityFrameworkCore;
using ProfanityService;
using ProfanityService.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("ProfanityService");
builder.Services.AddDbContext<ProfanityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Profanity")));

var app = builder.Build();
app.UseObservability();

await StartupRetry.RunAsync(async () =>
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ProfanityDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Words.AnyAsync())
    {
        db.Words.AddRange(ProfanityDbContext.SeedWords.Select(w => new ProhibitedWord { Value = w }));
        await db.SaveChangesAsync();
    }
}, app.Logger, "Creating profanity schema");

var profanity = app.MapGroup("/api/profanity");

profanity.MapPost("/filter", async (FilterRequest request, ProfanityDbContext db, ILogger<Program> logger) =>
{
    var words = await db.Words.AsNoTracking().Select(w => w.Value).ToListAsync();
    var result = ProfanityFilter.Apply(request.Text, words);
    if (result.ContainedProfanity)
        logger.LogInformation("Filtered {MatchCount} prohibited word(s)", result.Matches.Length);
    return Results.Ok(result);
});

profanity.MapGet("/words", async (ProfanityDbContext db) =>
    await db.Words.AsNoTracking().OrderBy(w => w.Value).Select(w => w.Value).ToListAsync());

profanity.MapPost("/words", async (WordRequest request, ProfanityDbContext db, ILogger<Program> logger) =>
{
    var value = request.Word.Trim().ToLowerInvariant();
    if (value.Length == 0) return Results.BadRequest(new { error = "Word is required." });
    if (await db.Words.AnyAsync(w => w.Value == value)) return Results.Conflict(new { error = $"'{value}' already exists." });

    db.Words.Add(new ProhibitedWord { Value = value });
    await db.SaveChangesAsync();
    logger.LogInformation("Added prohibited word {Word}", value);
    return Results.Created($"/api/profanity/words/{value}", value);
});

profanity.MapDelete("/words/{word}", async (string word, ProfanityDbContext db, ILogger<Program> logger) =>
{
    var value = word.ToLowerInvariant();
    var deleted = await db.Words.Where(w => w.Value == value).ExecuteDeleteAsync();
    if (deleted == 0) return Results.NotFound();

    logger.LogInformation("Removed prohibited word {Word}", value);
    return Results.NoContent();
});

app.Run();

record WordRequest(string Word);
