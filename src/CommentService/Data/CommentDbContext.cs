using Microsoft.EntityFrameworkCore;

namespace CommentService.Data;

public enum CommentStatus
{
    Approved,
    PendingModeration,
}

public class Comment
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public required string Author { get; set; }
    public required string Content { get; set; }
    public CommentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CommentDbContext(DbContextOptions<CommentDbContext> options) : DbContext(options)
{
    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Comment>(comment =>
        {
            comment.Property(c => c.Author).HasMaxLength(200);
            comment.Property(c => c.Content).HasMaxLength(4000);
            comment.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
            comment.HasIndex(c => new { c.ArticleId, c.Status });
        });
    }
}
