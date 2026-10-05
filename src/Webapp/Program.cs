using HappyHeadlines.Shared.Observability;
using Microsoft.AspNetCore.DataProtection;
using Webapp;

var builder = WebApplication.CreateBuilder(args);
builder.AddObservability("Webapp");
builder.Services.AddRazorPages();

// Antiforgery cookies are encrypted with these keys, so they must survive container rebuilds.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));

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
