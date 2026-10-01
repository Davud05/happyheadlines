using System.Text.RegularExpressions;

namespace ProfanityService;

public static class ProfanityFilter
{
    public static FilterResult Apply(string text, IReadOnlyCollection<string> words)
    {
        if (words.Count == 0 || string.IsNullOrEmpty(text)) return new FilterResult(text, []);

        var pattern = $@"\b({string.Join('|', words.Select(Regex.Escape))})\b";
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var filtered = Regex.Replace(text, pattern, match =>
        {
            matches.Add(match.Value.ToLowerInvariant());
            return new string('*', match.Length);
        }, RegexOptions.IgnoreCase);

        return new FilterResult(filtered, [.. matches]);
    }
}

public record FilterRequest(string Text);

public record FilterResult(string Text, string[] Matches)
{
    public bool ContainedProfanity => Matches.Length > 0;
}
