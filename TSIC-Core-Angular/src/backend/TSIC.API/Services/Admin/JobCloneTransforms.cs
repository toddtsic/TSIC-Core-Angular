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

    // ── Agegroup name year-bump ────────────────────────────────

    /// <summary>
    /// Finds 4-digit year patterns (2020–2039) in a string and increments each by 1.
    /// E.g., "2025 Boys" → "2026 Boys", "Class of 2027" → "Class of 2028".
    /// No-op for names without a year token (e.g., "Boys Advanced" stays "Boys Advanced").
    /// </summary>
    public static string IncrementYearsInName(string name)
    {
        return Regex.Replace(name, @"\b(20[2-3]\d)\b", m =>
            (int.Parse(m.Value) + 1).ToString());
    }
}
