using Microsoft.EntityFrameworkCore;

namespace SubscriberService.Data;

public class Subscriber
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public DateTimeOffset SubscribedAt { get; set; }
}

public class SubscriberDbContext(DbContextOptions<SubscriberDbContext> options) : DbContext(options)
{
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Subscriber>(subscriber =>
        {
            subscriber.Property(s => s.Email).HasMaxLength(SubscriberRules.MaxEmailLength);
            subscriber.HasIndex(s => s.Email).IsUnique();
        });
    }
}
