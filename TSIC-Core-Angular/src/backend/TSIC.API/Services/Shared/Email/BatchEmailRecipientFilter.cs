using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Pure recipient-address rules shared by every batch path (parity with the schedule path):
/// trims, drops blanks, drops the <c>not@given.com</c> missing-email sentinel, drops obviously
/// invalid addresses (no '@'), and de-duplicates case-insensitively. No I/O — directly unit-testable.
/// </summary>
public static class BatchEmailRecipientFilter
{
    public const string MissingEmailSentinel = "not@given.com";

    /// <summary>
    /// Resolves all sendable addresses for one batch recipient from the bulk-loaded address maps.
    /// Player → mom + dad + the player's own email (distinct, case-insensitive); every other role →
    /// the registrant's own User.Email. The player's own email is OPTIONAL (child accounts often have
    /// none). Applies the same blank/sentinel/invalid stripping as <see cref="BuildSendableSet"/>.
    /// Pure (no I/O) so it is directly unit-testable; the caller bulk-loads the maps once per batch.
    /// </summary>
    public static List<string> ResolveRecipients(
        string? roleId,
        string? familyUserId,
        Guid registrationId,
        IReadOnlyDictionary<Guid, string?> emailByRegId,
        IReadOnlyDictionary<string, BatchFamilyEmailsDto> familyEmailsById)
    {
        emailByRegId.TryGetValue(registrationId, out var ownEmail);

        if (roleId == RoleConstants.Player && !string.IsNullOrWhiteSpace(familyUserId)
            && familyEmailsById.TryGetValue(familyUserId, out var family))
        {
            return BuildSendableSet(new[] { family.MomEmail, family.DadEmail, ownEmail });
        }

        return BuildSendableSet(new[] { ownEmail });
    }

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
