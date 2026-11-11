using HappyHeadlines.Shared.Features;
using HappyHeadlines.Shared.Observability;
using Microsoft.Extensions.Http.Resilience;
using Website;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("Website");
builder.Services.AddRazorPages();

builder.Services.AddHttpClient<ArticleClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Articles"] ?? "http://localhost:8000"))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<CommentClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Comments"] ?? "http://localhost:8001"))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<SubscriberClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Subscribers"] ?? "http://localhost:8006"))
    .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddFeatureToggles();
var app = builder.Build();
app.UseObservability();

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

app.UseRouting();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
