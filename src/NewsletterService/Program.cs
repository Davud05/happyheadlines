using HappyHeadlines.Shared.Messaging;
using HappyHeadlines.Shared.Observability;
using NewsletterService;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("NewsletterService");
builder.Services.AddMessaging();

builder.Services.AddHttpClient<ArticleClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Articles"] ?? "http://localhost:8000"))
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<EmailSender>();
builder.Services.AddScoped<Newsletter>();
builder.Services.AddHostedService<BreakingNewsConsumer>();
builder.Services.AddHostedService<DailyNewsletterJob>();

var app = builder.Build();
app.UseObservability();

app.MapPost("/api/newsletter/daily", async (Newsletter newsletter, CancellationToken ct) =>
{
    await newsletter.SendDailyAsync(ct);
    return Results.Accepted();
});

app.Run();
