using HappyHeadlines.Shared.Observability;
using Webapp;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("Webapp");
builder.Services.AddRazorPages();

builder.Services.AddHttpClient<DraftClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Drafts"] ?? "http://localhost:8003"))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<PublisherClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Publisher"] ?? "http://localhost:8004"))
    .AddStandardResilienceHandler();

var app = builder.Build();
app.UseObservability();

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");

app.UseRouting();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
