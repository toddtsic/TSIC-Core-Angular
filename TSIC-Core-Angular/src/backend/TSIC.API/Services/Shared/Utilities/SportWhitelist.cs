namespace TSIC.API.Services.Shared.Utilities;

/// <summary>
/// Canonical whitelist of sports offered in sport dropdowns (AM-008). The Sports
/// reference table carries a long legacy list (camping, caving, kayaking, etc.) that
/// must not leak into pick-a-sport UIs; filter here rather than mutate the table so
/// historical references stay resolvable. Every sport actually in use by a job
/// (2026-07-27 census across 1,057 jobs) is present — removing an entry can blank an
/// existing job's Sport selection, so trim with a census, never by taste.
/// </summary>
public static class SportWhitelist
{
    private static readonly HashSet<string> AllowedSportNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "lacrosse", "soccer", "football", "hockey", "field hockey",
        "basketball", "baseball", "softball", "volleyball",
        "wrestling", "rugby", "cheerleading",
        // In production use (8 + 1 jobs) but absent from the original LADT-only list.
        "track and field", "multi-sport"
    };

    public static bool Contains(string? sportName) =>
        sportName != null && AllowedSportNames.Contains(sportName);

    /// <summary>"lacrosse" → "Lacrosse", "field hockey" → "Field Hockey".</summary>
    public static string ToTitleCase(string name)
    {
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
