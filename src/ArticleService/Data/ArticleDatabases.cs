using HappyHeadlines.Shared;
using HappyHeadlines.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ArticleService.Data;

/// <summary>
/// Z-axis split: one database per continent plus a global database.
/// The continent of a request decides which database it is routed to.
/// </summary>
public sealed class ArticleDatabases
{
    private readonly Dictionary<string, DbContextOptions<ArticleDbContext>> _options;

    public ArticleDatabases(IConfiguration configuration)
    {
        _options = Continents.All.ToDictionary(
            continent => continent,
            continent => new DbContextOptionsBuilder<ArticleDbContext>()
                .UseNpgsql(configuration.GetConnectionString(continent)
                    ?? throw new InvalidOperationException($"Missing connection string for {continent}."))
                .Options);
    }

    public ArticleDbContext Open(string continent) => new(_options[continent]);

    public async Task EnsureCreatedAsync(ILogger logger)
    {
        foreach (var continent in Continents.All)
        {
            await StartupRetry.RunAsync(async () =>
            {
                await using var db = Open(continent);
                await db.Database.EnsureCreatedAsync();
            }, logger, $"Creating schema in {continent} database");
        }
    }
}
