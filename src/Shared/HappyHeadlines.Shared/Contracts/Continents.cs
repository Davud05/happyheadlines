namespace HappyHeadlines.Shared.Contracts;

public static class Continents
{
    public const string Global = "Global";

    public static readonly IReadOnlyList<string> All =
    [
        "Africa", "Antarctica", "Asia", "Europe", "NorthAmerica", "Oceania", "SouthAmerica", Global
    ];

    public static bool TryNormalize(string? value, out string continent)
    {
        continent = All.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)) ?? "";
        return continent.Length > 0;
    }
}
