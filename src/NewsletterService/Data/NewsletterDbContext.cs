using Microsoft.EntityFrameworkCore;

namespace NewsletterService.Data;

/// <summary>
/// NewsletterService's own copy of a subscription, built from SubscriberQueue events.
/// Unsubscribed rows are kept (inactive) so a late, older event cannot resurrect them.
/// </summary>
public class NewsletterSubscriber
{
    public required string Email { get; set; }
    public Guid SubscriberId { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

public class NewsletterDbContext(DbContextOptions<NewsletterDbContext> options) : DbContext(options)
{
    public DbSet<NewsletterSubscriber> Subscribers => Set<NewsletterSubscriber>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<NewsletterSubscriber>(subscriber =>
        {
            subscriber.HasKey(s => s.Email);
            subscriber.Property(s => s.Email).HasMaxLength(320);
        });
    }
}
