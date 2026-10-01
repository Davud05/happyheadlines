using Microsoft.EntityFrameworkCore;

namespace ProfanityService.Data;

public class ProhibitedWord
{
    public int Id { get; set; }
    public required string Value { get; set; }
}

public class ProfanityDbContext(DbContextOptions<ProfanityDbContext> options) : DbContext(options)
{
    public DbSet<ProhibitedWord> Words => Set<ProhibitedWord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ProhibitedWord>(word =>
        {
            word.Property(w => w.Value).HasMaxLength(100);
            word.HasIndex(w => w.Value).IsUnique();
        });
    }

    public static readonly string[] SeedWords =
        ["damn", "hell", "crap", "idiot", "stupid", "moron", "dumb", "shit", "bastard", "loser"];
}
