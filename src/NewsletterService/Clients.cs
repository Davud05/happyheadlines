namespace NewsletterService;

public record ArticleDto(Guid Id, string Title, string Content, string Author, string Continent, DateTimeOffset PublishedAt);

public sealed class ArticleClient(HttpClient http)
{
    public async Task<List<ArticleDto>> GetSinceAsync(string continent, DateTimeOffset since, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ArticleDto>>(
            $"/api/articles?continent={continent}&limit=10&since={Uri.EscapeDataString(since.ToString("o"))}", ct) ?? [];
}
