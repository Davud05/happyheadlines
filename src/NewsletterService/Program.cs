using HappyHeadlines.Shared;
using HappyHeadlines.Shared.Features;
using HappyHeadlines.Shared.Messaging;
using HappyHeadlines.Shared.Observability;
using Microsoft.EntityFrameworkCore;
using NewsletterService;
using NewsletterService.Data;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("NewsletterService");
builder.Services.AddMessaging();
builder.Services.AddFeatureToggles();
builder.Services.AddDbContext<NewsletterDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Newsletter")));

builder.Services.AddHttpClient<ArticleClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Articles"] ?? "http://localhost:8000"))
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<EmailSender>();
builder.Services.AddScoped<SubscriberDirectory>();
builder.Services.AddScoped<Newsletter>();
builder.Services.AddHostedService<BreakingNewsConsumer>();
builder.Services.AddHostedService<SubscriptionConsumer>();
builder.Services.AddHostedService<DailyNewsletterJob>();

var app = builder.Build();
app.UseObservability();

await StartupRetry.RunAsync(async () =>
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NewsletterDbContext>().Database.EnsureCreatedAsync();
}, app.Logger, "Creating newsletter schema");

app.MapPost("/api/newsletter/daily", async (Newsletter newsletter, CancellationToken ct) =>
{
    await newsletter.SendDailyAsync(ct);
    return Results.Accepted();
});

app.Run();
