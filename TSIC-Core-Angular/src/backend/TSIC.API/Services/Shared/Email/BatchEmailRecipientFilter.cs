namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Pure recipient-address rules shared by every batch path (parity with the schedule path):
/// trims, drops blanks, drops the <c>not@given.com</c> missing-email sentinel, drops obviously
/// invalid addresses (no '@'), and de-duplicates case-insensitively. No I/O — directly unit-testable.
/// </summary>
public static class BatchEmailRecipientFilter
{
    public const string MissingEmailSentinel = "not@given.com";

    /// <summary>Returns the distinct, sendable subset of the candidate addresses (order preserved).</summary>
    public static List<string> BuildSendableSet(IEnumerable<string?> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!IsSendable(candidate)) continue;
            var trimmed = candidate!.Trim();
            if (seen.Add(trimmed)) result.Add(trimmed);
        }
        return result;
    }

    /// <summary>True if the address is non-blank, not the sentinel, and looks like an address.</summary>
    public static bool IsSendable(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var trimmed = email.Trim();
        if (string.Equals(trimmed, MissingEmailSentinel, StringComparison.OrdinalIgnoreCase)) return false;
        return trimmed.Contains('@');
    }
}
