using HappyHeadlines.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Website.Pages;

public class IndexModel(ArticleClient articles, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Region { get; set; } = Continents.Global;

    public ArticleDto? Focus { get; private set; }
    public List<ArticleDto> Others { get; private set; } = [];
    public string? Error { get; private set; }
    public IReadOnlyList<string> AllContinents => Continents.All;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Region = Continents.TryNormalize(Region, out var region) ? region : Continents.Global;

        try
        {
            var global = articles.LatestAsync(Continents.Global, 10, ct);
            var regional = Region == Continents.Global ? Task.FromResult(new List<ArticleDto>()) : articles.LatestAsync(Region, 10, ct);

            var latest = (await regional).Concat(await global)
                .OrderByDescending(a => a.PublishedAt)
                .Take(10)
                .ToList();

            Focus = latest.FirstOrDefault();
            Others = latest.Skip(1).ToList();
        }
        catch (Exception ex) when (Downstream.IsUnavailable(ex))
        {
            logger.LogWarning("ArticleService unavailable: {Reason}", ex.Message);
            Error = "We can't reach the newsroom right now. Please try again in a moment.";
        }
    }
}
