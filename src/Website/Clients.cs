using System.Net;

namespace Website;

public static class Downstream
{
    public static bool IsUnavailable(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or Polly.ExecutionRejectedException;
}

public record ArticleDto(Guid Id, string Title, string Content, string Author, string Continent, DateTimeOffset PublishedAt);

public record CommentDto(Guid Id, Guid ArticleId, string Author, string Content, DateTimeOffset CreatedAt);

public sealed class ArticleClient(HttpClient http)
{
    public async Task<List<ArticleDto>> LatestAsync(string continent, int limit, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ArticleDto>>($"/api/articles?continent={continent}&limit={limit}", ct) ?? [];

    public async Task<ArticleDto?> GetAsync(string continent, Guid id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/articles/{id}?continent={continent}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ArticleDto>(ct);
    }
}

public sealed class CommentClient(HttpClient http)
{
    public async Task<List<CommentDto>> ForArticleAsync(Guid articleId, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<CommentDto>>($"/api/comments?articleId={articleId}", ct) ?? [];

    /// <summary>Returns true when the comment is visible immediately, false when it is held for moderation.</summary>
    public async Task<bool> PostAsync(Guid articleId, string author, string content, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/api/comments", new { articleId, author, content }, ct);
        response.EnsureSuccessStatusCode();
        return response.StatusCode == HttpStatusCode.Created;
    }
}