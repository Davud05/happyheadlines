using System.Net.Mail;

namespace SubscriberService;

public static class SubscriberRules
{
    public const int MaxEmailLength = 320;

    /// <summary>Accepts a plain address like "anna@example.com" and returns it trimmed and lower-cased.</summary>
    public static bool TryNormalizeEmail(string? input, out string email)
    {
        email = "";
        var trimmed = input?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxEmailLength) return false;

        // MailAddress also accepts display names ("Anna <anna@example.com>"); only bare addresses are allowed.
        if (!MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed) return false;
        if (!address.Host.Contains('.')) return false;

        email = trimmed.ToLowerInvariant();
        return true;
    }
}
