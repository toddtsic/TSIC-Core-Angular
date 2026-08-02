using System.Text.RegularExpressions;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Pure-function transforms used by JobCloneService. Extracted for unit testing.
/// All methods deterministic, no I/O, no dependencies.
/// </summary>
public static class JobCloneTransforms
{
    // ── Year-delta computation ─────────────────────────────────

    /// <summary>
    /// Year delta for seasonal date shifts. Returns 0 if either year isn't parseable.
    /// </summary>
    public static int ComputeYearDelta(string? sourceYear, string? targetYear)
    {
        if (!int.TryParse(sourceYear, out var src) || !int.TryParse(targetYear, out var tgt))
            return 0;
        return tgt - src;
    }

    // ── Year-delta date shifts ─────────────────────────────────
    // DateTime.AddYears / DateOnly.AddYears clamp Feb-29 to Feb-28 in non-leap years.

    public static DateTime ShiftByYears(DateTime date, int years) =>
        years == 0 ? date : date.AddYears(years);

    public static DateTime? ShiftByYears(DateTime? date, int years) =>
        date.HasValue ? ShiftByYears(date.Value, years) : null;

    public static DateOnly ShiftByYears(DateOnly date, int years) =>
        years == 0 ? date : date.AddYears(years);

    public static DateOnly? ShiftByYears(DateOnly? date, int years) =>
        date.HasValue ? ShiftByYears(date.Value, years) : null;

    // ── Year-token bump (names + content) ─────────────────────

    /// <summary>
    /// Finds 4-digit year tokens (2000–2099) in a string and increments each by 1.
    /// E.g., "2025 Boys" → "2026 Boys", "Class of 2027" → "Class of 2028".
    /// No-op for names without a year token (e.g., "Boys Advanced" stays "Boys Advanced").
    /// Pattern is the full century (\b20\d{2}\b), not a hardcoded decade — the previous
    /// [2-3]\d range would have silently stopped matching in 2040.
    /// </summary>
    public static string IncrementYearsInName(string name)
    {
        return Regex.Replace(name, @"\b(20\d{2})\b", m =>
            (int.Parse(m.Value) + 1).ToString());
    }

    /// <summary>
    /// Null-tolerant year-token bump for long-text content fields (confirmation emails,
    /// waivers, bulletin bodies, parallax texts). Known caveat, accepted by design: EVERY
    /// 20xx token bumps, including non-seasonal years in prose ("Est. 2021") — content is
    /// expert-reviewed on the verify page before release.
    /// </summary>
    public static string? BumpYears(string? text) =>
        string.IsNullOrEmpty(text) ? text : IncrementYearsInName(text);
}
