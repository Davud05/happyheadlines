namespace CommentService.Data;

/// <summary>Example comments inserted into an empty database on first start, attached to ArticleService's seeded articles.</summary>
public static class SeedComments
{
    private static readonly Guid GlobalRenewables = Guid.Parse("a0000000-0000-0000-0000-000008000001");
    private static readonly Guid GlobalChildMortality = Guid.Parse("a0000000-0000-0000-0000-000008000002");
    private static readonly Guid EuropeTrees = Guid.Parse("a0000000-0000-0000-0000-000004000001");

    public static IEnumerable<Comment> Create()
    {
        var now = DateTimeOffset.UtcNow;
        return new (Guid ArticleId, string Author, string Content)[]
        {
            (GlobalRenewables, "Lena", "This gives me so much hope for the future!"),
            (GlobalRenewables, "Marcus", "Great to see the numbers finally going the right way."),
            (GlobalChildMortality, "Fatima", "Wonderful news, thank you for sharing."),
            (EuropeTrees, "Sofie", "I was there with my kids, it was amazing!"),
            (EuropeTrees, "Henrik", "More towns should do this."),
        }.Select((c, i) => new Comment
        {
            Id = Guid.NewGuid(),
            ArticleId = c.ArticleId,
            Author = c.Author,
            Content = c.Content,
            Status = CommentStatus.Approved,
            CreatedAt = now.AddMinutes(-(5 - i) * 10),
        });
    }
}
