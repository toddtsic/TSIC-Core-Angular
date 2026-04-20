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

    // ── League name inference ──────────────────────────────────

    /// <summary>
    /// Infer a new league name by detecting the source's pattern: separator + position of
    /// year and season tokens. Substitutes author-entered values into the same positions.
    /// Handles 3-token patterns (name, season, year) in any order, with any of [- _ whitespace]
    /// as separator. Falls back to the supplied fallback if pattern isn't detectable.
    ///
    /// Examples:
    ///   "STEPS-Spring-2025"  + ("STEPS","Fall","2026") → "STEPS-Fall-2026"
    ///   "STEPS Spring 2025"  + ("STEPS","Fall","2026") → "STEPS Fall 2026"
    ///   "2025_Spring_STEPS"  + ("STEPS","Fall","2026") → "2026_Fall_STEPS"
    ///   "MyLeague"           → fallback (no detectable pattern)
    /// </summary>
    public static string InferLeagueName(
        string? sourceLeagueName,
        string newLeagueName,
        string seasonTarget,
        string yearTarget,
        string fallback)
    {
        if (string.IsNullOrEmpty(sourceLeagueName))
            return fallback;

        // Detect separator (first char from [- _ whitespace]).
        var sepMatch = Regex.Match(sourceLeagueName, @"[-_\s]");
        if (!sepMatch.Success)
            return fallback; // single-token source — no detectable pattern

        string sep = sepMatch.Value;
        var parts = sourceLeagueName.Split(sep);
        if (parts.Length != 3)
            return fallback; // only handle exact 3-token pattern

        var yearIdx = Array.FindIndex(parts, p => Regex.IsMatch(p, @"^(19|20)\d\d$"));
        var seasonIdx = Array.FindIndex(parts, p =>
            Regex.IsMatch(p, @"^(Spring|Summer|Fall|Winter|Autumn)$", RegexOptions.IgnoreCase));

        if (yearIdx < 0)
            return fallback;

        var result = new string[3];
        for (int i = 0; i < 3; i++)
        {
            if (i == yearIdx) result[i] = yearTarget;
            else if (i == seasonIdx) result[i] = seasonTarget;
            else result[i] = newLeagueName;
        }

        return string.Join(sep, result);
    }
}
