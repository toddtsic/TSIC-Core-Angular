using System.Text.RegularExpressions;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Maps agegroup names across tournament years by offsetting graduation years.
/// "2026 Boys" + delta=1 → "2027 Boys". Non-year names are unchanged.
/// Handles combo years like "2030/2031 Girls" → "2031/2032 Girls".
/// </summary>
public static partial class AgegroupNameMapper
{
    // Matches 4-digit graduation years (2000–2099) as whole words
    [GeneratedRegex(@"\b(20\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex YearRx();

    /// <summary>
    /// Offset all graduation years found in <paramref name="name"/> by <paramref name="delta"/>.
    /// Returns the original string unchanged if no years are found.
    /// </summary>
    public static string OffsetName(string name, int delta)
    {
        if (delta == 0 || string.IsNullOrEmpty(name)) return name;

        return YearRx().Replace(name, m =>
        {
            var year = int.Parse(m.Value);
            return (year + delta).ToString();
        });
    }

    /// <summary>
    /// Build a mapping from source agegroup names to current-year names.
    /// Only includes entries where the name actually changed (contains a year).
    /// </summary>
    public static Dictionary<string, string> BuildNameMap(
        IEnumerable<string> sourceNames, int yearDelta)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (yearDelta == 0) return map;

        foreach (var name in sourceNames)
        {
            var offset = OffsetName(name, yearDelta);
            if (!string.Equals(name, offset, StringComparison.OrdinalIgnoreCase))
                map[name] = offset;
        }
        return map;
    }

    /// <summary>
    /// Returns true if the name contains a graduation year (e.g., "2026 Boys").
    /// </summary>
    public static bool ContainsGradYear(string name)
        => !string.IsNullOrEmpty(name) && YearRx().IsMatch(name);

    /// <summary>
    /// Extract the first graduation year found in <paramref name="name"/>, or null.
    /// </summary>
    public static int? ExtractFirstYear(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var m = YearRx().Match(name);
        return m.Success ? int.Parse(m.Value) : null;
    }
}
