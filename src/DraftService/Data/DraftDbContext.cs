using Microsoft.EntityFrameworkCore;

namespace DraftService.Data;

public class Draft
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public required string Author { get; set; }
    public required string Continent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class DraftDbContext(DbContextOptions<DraftDbContext> options) : DbContext(options)
{
    public DbSet<Draft> Drafts => Set<Draft>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Draft>(draft =>
        {
            draft.Property(d => d.Title).HasMaxLength(300);
            draft.Property(d => d.Author).HasMaxLength(200);
            draft.Property(d => d.Continent).HasMaxLength(50);
            draft.HasIndex(d => d.Author);
        });
    }
}
