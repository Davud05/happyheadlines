namespace ArticleService.Data;

public class Article
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public required string Author { get; set; }
    public required string Continent { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
