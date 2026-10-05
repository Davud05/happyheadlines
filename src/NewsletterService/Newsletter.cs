using System.Net;
using HappyHeadlines.Shared.Contracts;

namespace NewsletterService;

public sealed class Newsletter(
    ArticleClient articles,
    EmailSender email,
    IConfiguration configuration,
    ILogger<Newsletter> logger)
{
    private string[] Subscribers => configuration.GetSection("Newsletter:Subscribers").Get<string[]>() ?? [];

    /// <summary>Immediate newsletter sent as soon as an article is published.</summary>
    public async Task SendBreakingNewsAsync(ArticlePublished article, CancellationToken ct)
    {
        var recipients = Subscribers;
        var body = $"<h1>Just published</h1>{RenderArticle(article.Title, article.Content, article.Author)}";

        foreach (var recipient in recipients)
            await email.SendAsync(recipient, $"Breaking good news: {article.Title}", body, ct);

        logger.LogInformation("Sent breaking news for article {ArticleId} to {Recipients} subscriber(s)",
            article.Id, recipients.Length);
    }

    /// <summary>Daily digest with the last 24 hours of global articles, requested from ArticleService.</summary>
    public async Task SendDailyAsync(CancellationToken ct)
    {
        var digest = await articles.GetSinceAsync(Continents.Global, DateTimeOffset.UtcNow.AddDays(-1), ct);
        var recipients = Subscribers;
        if (digest.Count == 0)
        {
            logger.LogInformation("Daily newsletter skipped, no articles from the last 24 hours");
            return;
        }

        var body = "<h1>Your daily Happy Headlines</h1>"
            + string.Concat(digest.Select(a => RenderArticle(a.Title, a.Content, a.Author)));

        foreach (var recipient in recipients)
            await email.SendAsync(recipient, "Your daily dose of good news", body, ct);

        logger.LogInformation("Daily newsletter with {Articles} article(s) sent to {Recipients} subscriber(s)",
            digest.Count, recipients.Length);
    }

    private static string RenderArticle(string title, string content, string author) =>
        $"<h2>{WebUtility.HtmlEncode(title)}</h2><p><i>by {WebUtility.HtmlEncode(author)}</i></p>"
        + $"<p>{WebUtility.HtmlEncode(content)}</p><hr/>";
}
