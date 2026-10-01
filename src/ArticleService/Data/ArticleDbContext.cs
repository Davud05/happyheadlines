using Microsoft.EntityFrameworkCore;

namespace ArticleService.Data;

public class ArticleDbContext(DbContextOptions<ArticleDbContext> options) : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Article>(article =>
        {
            article.Property(a => a.Title).HasMaxLength(300);
            article.Property(a => a.Author).HasMaxLength(200);
            article.Property(a => a.Continent).HasMaxLength(50);
            article.HasIndex(a => a.PublishedAt);
        });
    }
}
