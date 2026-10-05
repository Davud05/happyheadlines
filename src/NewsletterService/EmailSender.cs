using System.Diagnostics;
using System.Net.Mail;

namespace NewsletterService;

public sealed class EmailSender(IConfiguration configuration)
{
    private static readonly ActivitySource Tracer = new("NewsletterService");

    private readonly string _host = configuration["Smtp:Host"] ?? "localhost";
    private readonly int _port = int.TryParse(configuration["Smtp:Port"], out var port) ? port : 1025;
    private const string From = "newsletter@happyheadlines.com";

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        using var activity = Tracer.StartActivity("smtp send", ActivityKind.Client);
        activity?.SetTag("email.subject", subject);

        using var client = new SmtpClient(_host, _port);
        using var message = new MailMessage(From, to, subject, htmlBody) { IsBodyHtml = true };
        await client.SendMailAsync(message, ct);
    }
}
