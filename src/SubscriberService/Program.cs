using HappyHeadlines.Shared;
using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Features;
using HappyHeadlines.Shared.Messaging;
using HappyHeadlines.Shared.Observability;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SubscriberService;
using SubscriberService.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("SubscriberService");
builder.Services.AddDbContext<SubscriberDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Subscribers")));
builder.Services.AddMessaging();
builder.Services.AddFeatureToggles();

var app = builder.Build();
app.UseObservability();

await StartupRetry.RunAsync(async () =>
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<SubscriberDbContext>().Database.EnsureCreatedAsync();
}, app.Logger, "Creating subscriber schema");

var publishTimeout = TimeSpan.FromSeconds(5);
var subscribers = app.MapGroup("/api/subscribers");

// Release toggle: while SUBSCRIBER_SERVICE is off the service is deployed but not released.
subscribers.AddEndpointFilter(async (context, next) =>
    context.HttpContext.RequestServices.GetRequiredService<IFeatureToggles>().IsEnabled(FeatureFlags.SubscriberService)
        ? await next(context)
        : Results.Problem("Subscriptions are not available yet.", statusCode: StatusCodes.Status503ServiceUnavailable));

subscribers.MapGet("/", async (SubscriberDbContext db, CancellationToken ct) =>
    await db.Subscribers.AsNoTracking().OrderBy(s => s.SubscribedAt).ToListAsync(ct));

subscribers.MapGet("/{id:guid}", async (Guid id, SubscriberDbContext db, CancellationToken ct) =>
    await db.Subscribers.FindAsync([id], ct) is { } subscriber ? Results.Ok(subscriber) : Results.NotFound());

subscribers.MapPost("/", async (SubscribeRequest request, SubscriberDbContext db, MessagePublisher publisher,
    ILogger<Program> logger, CancellationToken ct) =>
{
    if (!SubscriberRules.TryNormalizeEmail(request.Email, out var email))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["Email"] = ["A valid email address is required."] });
    if (await db.Subscribers.AnyAsync(s => s.Email == email, ct))
        return Results.Conflict(new { error = "This email address is already subscribed." });

    var subscriber = new Subscriber { Id = Guid.NewGuid(), Email = email, SubscribedAt = DateTimeOffset.UtcNow };

    // The subscriber is only committed once SubscriberQueue has confirmed the event, so a subscriber
    // can never exist without NewsletterService eventually hearing about it.
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    db.Subscribers.Add(subscriber);
    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
        return Results.Conflict(new { error = "This email address is already subscribed." });
    }

    var published = await TryPublishAsync(publisher,
        new SubscriptionChanged(subscriber.Id, email, Subscribed: true, subscriber.SubscribedAt), logger, ct);
    if (!published) return QueueUnavailable();

    await transaction.CommitAsync(ct);
    logger.LogInformation("Subscriber {SubscriberId} subscribed", subscriber.Id);
    return Results.Created($"/api/subscribers/{subscriber.Id}", subscriber);
});

subscribers.MapDelete("/{id:guid}", async (Guid id, SubscriberDbContext db, MessagePublisher publisher,
    ILogger<Program> logger, CancellationToken ct) =>
{
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    var subscriber = await db.Subscribers.FindAsync([id], ct);
    if (subscriber is null) return Results.NotFound();

    db.Subscribers.Remove(subscriber);
    await db.SaveChangesAsync(ct);

    var published = await TryPublishAsync(publisher,
        new SubscriptionChanged(id, subscriber.Email, Subscribed: false, DateTimeOffset.UtcNow), logger, ct);
    if (!published) return QueueUnavailable();

    await transaction.CommitAsync(ct);
    logger.LogInformation("Subscriber {SubscriberId} unsubscribed", id);
    return Results.NoContent();
});

app.Run();

async Task<bool> TryPublishAsync(MessagePublisher publisher, SubscriptionChanged message, ILogger logger, CancellationToken ct)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(publishTimeout);
    try
    {
        await publisher.PublishAsync(Exchanges.Subscribers, message, timeout.Token);
        return true;
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
        logger.LogWarning("SubscriberQueue unavailable, rolling back subscription change for {SubscriberId}: {Reason}",
            message.SubscriberId, ex.Message);
        return false;
    }
}

static IResult QueueUnavailable() =>
    Results.Problem("Subscriptions cannot be changed right now. Please try again shortly.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

record SubscribeRequest(string? Email);
