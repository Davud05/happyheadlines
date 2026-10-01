using Polly.CircuitBreaker;
using Polly.Timeout;

namespace CommentService.Profanity;

public sealed class ProfanityClient(HttpClient http)
{
    public async Task<string> FilterAsync(string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/api/profanity/filter", new { text }, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FilterResponse>(ct);
        return result?.Text ?? throw new InvalidOperationException("ProfanityService returned an empty response.");
    }

    public static bool IsUnavailable(Exception ex) =>
        ex is BrokenCircuitException or TimeoutRejectedException or HttpRequestException or TaskCanceledException;

    private record FilterResponse(string Text);
}
