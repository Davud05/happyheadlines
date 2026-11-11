using HappyHeadlines.Shared.Contracts;
using HappyHeadlines.Shared.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NewsletterService;
using NewsletterService.Data;

namespace HappyHeadlines.Tests;

public class SubscriberDirectoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly DbContextOptions<NewsletterDbContext> _options = new DbContextOptionsBuilder<NewsletterDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Newsletter:Subscribers:0"] = "legacy-1@example.com",
            ["Newsletter:Subscribers:1"] = "legacy-2@example.com",
        })
        .Build();

    // A new context per call, like a new DI scope per message in the service.
    private SubscriberDirectory Directory(bool released) =>
        new(new NewsletterDbContext(_options), new FixedToggles(released), _configuration, NullLogger<SubscriberDirectory>.Instance);

    private static SubscriptionChanged Change(string email, bool subscribed, DateTimeOffset at) =>
        new(Guid.NewGuid(), email, subscribed, at);

    [Fact]
    public async Task Toggle_off_sends_to_the_configured_legacy_list()
    {
        await Directory(released: false).ApplyAsync(Change("new@example.com", true, T0), default);

        var recipients = await Directory(released: false).GetRecipientsAsync(default);

        Assert.Equal(["legacy-1@example.com", "legacy-2@example.com"], recipients);
    }

    [Fact]
    public async Task Toggle_on_sends_only_to_active_subscribers_from_SubscriberQueue()
    {
        await Directory(true).ApplyAsync(Change("anna@example.com", true, T0), default);
        await Directory(true).ApplyAsync(Change("lars@example.com", true, T0), default);
        await Directory(true).ApplyAsync(Change("lars@example.com", false, T0.AddMinutes(1)), default);

        var recipients = await Directory(released: true).GetRecipientsAsync(default);

        Assert.Equal(["anna@example.com"], recipients);
    }

    [Fact]
    public async Task Older_event_arriving_late_does_not_undo_a_newer_one()
    {
        await Directory(true).ApplyAsync(Change("anna@example.com", false, T0.AddMinutes(5)), default);
        await Directory(true).ApplyAsync(Change("anna@example.com", true, T0), default);

        Assert.Empty(await Directory(true).GetRecipientsAsync(default));
    }

    [Fact]
    public async Task Replayed_event_is_applied_only_once()
    {
        var change = Change("Anna@Example.com", true, T0);

        await Directory(true).ApplyAsync(change, default);
        await Directory(true).ApplyAsync(change, default);

        await using var db = new NewsletterDbContext(_options);
        var stored = Assert.Single(await db.Subscribers.ToListAsync());
        Assert.Equal("anna@example.com", stored.Email);
    }

    [Fact]
    public async Task Resubscribing_after_unsubscribing_makes_the_subscriber_active_again()
    {
        await Directory(true).ApplyAsync(Change("anna@example.com", true, T0), default);
        await Directory(true).ApplyAsync(Change("anna@example.com", false, T0.AddDays(1)), default);
        await Directory(true).ApplyAsync(Change("anna@example.com", true, T0.AddDays(2)), default);

        Assert.Equal(["anna@example.com"], await Directory(true).GetRecipientsAsync(default));
    }

    private sealed class FixedToggles(bool enabled) : IFeatureToggles
    {
        public bool IsEnabled(string feature) => enabled;
    }
}
