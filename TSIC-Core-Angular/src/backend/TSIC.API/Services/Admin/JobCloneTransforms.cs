using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    // ── Legacy HTML-entity decode (names) ──────────────────────

    /// <summary>
    /// Turns HTML-entity residue left by the old system back into the characters it stands for
    /// — <c>Hero&amp;#39;s Tryouts</c> → <c>Hero's Tryouts</c>, <c>Clinics &amp;amp; Leagues</c> →
    /// <c>Clinics &amp; Leagues</c> (AM-076).
    ///
    /// The encoding is dead data, not an ongoing behaviour: nothing in the current write path
    /// produces it, and it survives only in rows minted by the legacy system. Decoding here — at
    /// the clone's name chokepoint — stops each new season inheriting its parent's residue, which
    /// a one-time row cleanup alone would not: the next clone would re-mint it.
    ///
    /// `&amp;`-free strings return unchanged, so the overwhelming majority of names never touch
    /// the decoder. Decoding is idempotent for our data: the artifacts are `&amp;#39;`/`&amp;amp;`/
    /// `&amp;quot;`, and a name that legitimately contains a bare `&amp;` decodes to itself.
    /// </summary>
    public static string? DecodeLegacyEntities(string? text) =>
        string.IsNullOrEmpty(text) || !text.Contains('&') ? text : WebUtility.HtmlDecode(text);

    // ── Grad-year dropdown roll-forward (Jobs.JsonOptions) ─────

    /// <summary>The two option lists inside JsonOptions whose entries are graduation years.</summary>
    private static readonly string[] GradYearOptionKeys =
        ["List_GradYears", "List_RecruitingGradYears"];

    /// <summary>
    /// Advances the graduation-year dropdowns inside a job's <c>JsonOptions</c> blob by
    /// <paramref name="yearDelta"/> (AM-078). The clone already advances the grad-year BOUNDS on
    /// age groups and teams, but these two player-facing lists rode CopyScalars verbatim — so a
    /// cloned job offered last season's years while its age groups had moved on.
    ///
    /// Deliberately conservative, because this column is also read by adult registration and the
    /// team LOP parse:
    ///   • Only the two lists above are touched; every other key is carried through untouched.
    ///   • Only entries that are ENTIRELY a 20xx year shift — "Other", "N/A", "2026 (rising)"
    ///     and anything else are left exactly as they were.
    ///   • Both Text and Value shift; legacy rows carry the year in Value with Text empty.
    ///   • Advance-only (<c>yearDelta &lt; 1</c> is a no-op), matching the name/age-group rules.
    ///   • Anything unparseable returns the ORIGINAL string — never a partial rewrite. The worst
    ///     case is the pre-AM-078 behaviour, not a corrupt blob.
    /// </summary>
    public static string? ShiftGradYearOptions(string? json, int yearDelta)
    {
        if (yearDelta < 1 || string.IsNullOrWhiteSpace(json))
            return json;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return json;

            var shifted = false;

            foreach (var key in GradYearOptionKeys)
            {
                if (FindProperty(root, key) is not JsonArray list)
                    continue;

                foreach (var item in list)
                {
                    if (item is not JsonObject entry)
                        continue;

                    shifted |= ShiftYearProperty(entry, "Text", yearDelta);
                    shifted |= ShiftYearProperty(entry, "Value", yearDelta);
                }
            }

            // No year tokens found — hand back the original text rather than a re-serialized
            // equivalent, so a clone that changes nothing leaves the blob byte-identical.
            return shifted ? root.ToJsonString() : json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>
    /// Case-tolerant property lookup. The blob's canonical casing is PascalCase, but it is
    /// decades-old hand-edited legacy data and a casing variant must not silently skip the shift.
    /// </summary>
    private static JsonNode? FindProperty(JsonObject obj, string name) =>
        ResolveKey(obj, name) is { } key ? obj[key] : null;

    /// <summary>
    /// The blob's ACTUAL key for <paramref name="name"/>, or null. Writes must go back through
    /// this — assigning to the canonical spelling when the blob holds a casing variant would add
    /// a second key beside the original instead of replacing it.
    /// </summary>
    private static string? ResolveKey(JsonObject obj, string name)
    {
        if (obj.ContainsKey(name))
            return name;

        foreach (var (key, _) in obj)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return key;
        }

        return null;
    }

    /// <summary>
    /// Shifts one option field when it holds nothing but a 20xx year. Returns whether it changed.
    /// </summary>
    private static bool ShiftYearProperty(JsonObject entry, string name, int yearDelta)
    {
        if (ResolveKey(entry, name) is not { } key || entry[key] is not JsonValue node)
            return false;

        if (!node.TryGetValue<string>(out var raw) || !Regex.IsMatch(raw, @"^20\d{2}$"))
            return false;

        entry[key] = (int.Parse(raw) + yearDelta).ToString();
        return true;
    }
}
