namespace DraftService.Data;

/// <summary>Example drafts inserted into an empty database on first start.</summary>
public static class SeedDrafts
{
    public static IEnumerable<Draft> Create()
    {
        var now = DateTimeOffset.UtcNow;
        return new (string Title, string Content, string Author, string Continent)[]
        {
            ("Community fridge feeds a whole neighbourhood", "Draft: interview the volunteers behind the fridge on Main Street.", "Mette Hansen", "Europe"),
            ("Scientists grow coral twice as fast", "Draft: explain the new lab technique and what it means for reefs.", "Olivia Wilson", "Oceania"),
            ("World's oldest library reopens", "Draft: history of the library and the restoration project.", "Ben Carter", "Global"),
        }.Select((d, i) => new Draft
        {
            Id = Guid.NewGuid(),
            Title = d.Title,
            Content = d.Content,
            Author = d.Author,
            Continent = d.Continent,
            CreatedAt = now.AddDays(-(i + 1)),
            UpdatedAt = now.AddHours(-(i + 1)),
        });
    }
}
