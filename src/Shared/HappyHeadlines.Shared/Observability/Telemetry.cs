using System.Diagnostics;

namespace HappyHeadlines.Shared.Observability;

public static class Telemetry
{
    public const string MessagingSourceName = "HappyHeadlines.Messaging";

    public static readonly ActivitySource Messaging = new(MessagingSourceName);
}
