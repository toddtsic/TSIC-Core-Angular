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
    /// True when this field name is the event-location row for this job. Compared on the full
    /// derived name, not just the prefix, so it stays correct if a job ever carries more than
    /// one pseudo-field.
    /// </summary>
    public static bool IsEventLocationFor(string? fieldName, string? jobPath)
    {
        var expected = NameForJobPath(jobPath);
        return expected is not null
            && string.Equals(fieldName?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
