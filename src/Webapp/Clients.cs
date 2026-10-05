namespace Webapp;

public record DraftDto(Guid Id, string Title, string Content, string Author, string Continent, DateTimeOffset UpdatedAt);

public record DraftInput(string Title, string Content, string Author, string Continent);

public sealed class DraftClient(HttpClient http)
{
    public async Task<List<DraftDto>> ListAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<DraftDto>>("/api/drafts", ct) ?? [];

    public Task<DraftDto?> GetAsync(Guid id, CancellationToken ct) =>
        http.GetFromJsonAsync<DraftDto>($"/api/drafts/{id}", ct);

    public async Task SaveAsync(Guid? id, DraftInput draft, CancellationToken ct)
    {
        using var response = id is null
            ? await http.PostAsJsonAsync("/api/drafts", draft, ct)
            : await http.PutAsJsonAsync($"/api/drafts/{id}", draft, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct) =>
        (await http.DeleteAsync($"/api/drafts/{id}", ct)).EnsureSuccessStatusCode();
}

public sealed class PublisherClient(HttpClient http)
{
    public async Task PublishAsync(DraftDto draft, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/api/publish",
            new { draft.Title, draft.Content, draft.Author, draft.Continent, DraftId = draft.Id }, ct);
        response.EnsureSuccessStatusCode();
    }
}
