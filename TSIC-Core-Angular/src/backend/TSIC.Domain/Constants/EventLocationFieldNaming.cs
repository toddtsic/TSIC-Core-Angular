namespace TSIC.Domain.Constants;

/// <summary>
/// THE naming rule for a job's event-location field — the pseudo-row in Fields that holds
/// the address sent to Vertical Insure for team RegSaver policies.
///
/// Legacy derives the name as "*" + the jobPath with its trailing five characters removed
/// (IRegistrationService.cs:1053 and :1395, where the variable is literally viJobFieldName),
/// then looks the event address up BY THAT NAME. Its refusal message calls it "an insurance
/// field". So:  americanselect-mainevent-2026  ->  *americanselect-mainevent
///
/// This lives in one place because three separate jobs depend on agreeing exactly: deciding
/// whether an existing row IS the event location, reading the address for the VI payload,
/// and creating the row when it is missing. Two hand-written copies of a string rule is how
/// a job silently stops being insurable.
/// </summary>
public static class EventLocationFieldNaming
{
    /// <summary>Marks a Fields row as a pseudo-field: real venues never start with this.</summary>
    public const string Prefix = "*";

    /// <summary>
    /// Characters legacy strips off the jobPath — the "-YYYY" suffix every jobPath carries.
    /// </summary>
    private const int YearSuffixLength = 5;

    /// <summary>
    /// The event-location field name for a job, or null when the jobPath is too short to
    /// carry a year suffix.
    ///
    /// Intentionally a blind five-character chop rather than a smarter "-YYYY" match: the rows
    /// already in the database were created under the blind rule, so anything cleverer would
    /// derive a name that does not match live data.
    /// </summary>
    public static string? NameForJobPath(string? jobPath)
    {
        var path = jobPath?.Trim();
        if (string.IsNullOrEmpty(path) || path.Length <= YearSuffixLength)
            return null;

        return Prefix + path[..^YearSuffixLength];
    }

    /// <summary>
    /// True when this field name carries the pseudo-field prefix -- an address row, not a
    /// place a game can be played. Every scheduling query filters on exactly this.
    /// </summary>
    public static bool IsPseudoField(string? fieldName) =>
        fieldName?.TrimStart().StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// True when this field name is the row derived from this job's own jobPath.
    /// </summary>
    public static bool IsEventLocationFor(string? fieldName, string? jobPath)
    {
        var expected = NameForJobPath(jobPath);
        return expected is not null
            && string.Equals(fieldName?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Picks the ONE attached pseudo-field whose address represents this event.
    ///
    /// The job's own derived name wins when it is present. Otherwise any attached pseudo-field
    /// will do, taken first by name for a stable answer: these rows hold a customer's standing
    /// address, and the same address recurs across their events (three Top Threat jobs share
    /// 15245 Bell Park Dr). Requiring the derived name would leave an event uninsurable purely
    /// because nobody created a row under the right string, when the correct address was
    /// already attached to it.
    ///
    /// Both the readiness flag shown to a director and the address put on the Vertical Insure
    /// payload MUST come from this one method. If they ever diverge, the UI reports an event as
    /// insurable while the quote fails on an empty address -- worse than failing visibly.
    /// </summary>
    public static T? SelectEventLocation<T>(
        IEnumerable<T>? candidates,
        Func<T, string?> nameOf,
        string? jobPath) where T : class
    {
        if (candidates is null) return null;

        var pseudoRows = candidates
            .Where(c => IsPseudoField(nameOf(c)))
            .OrderBy(c => nameOf(c), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return pseudoRows.FirstOrDefault(c => IsEventLocationFor(nameOf(c), jobPath))
            ?? pseudoRows.FirstOrDefault();
    }
}
