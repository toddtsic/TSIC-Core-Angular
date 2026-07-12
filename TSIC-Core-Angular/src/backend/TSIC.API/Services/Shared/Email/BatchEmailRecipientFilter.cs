using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared.Email;

/// <summary>
/// Recipient resolution for the batch paths: works out WHICH addresses belong to a recipient, then
/// filters them through <see cref="EmailAddressRules.IsSendable"/> and de-duplicates.
///
/// The address rule itself lives in <see cref="EmailAddressRules"/>, not here — the schedule path in
/// TSIC.Infrastructure needs the same rule and cannot reference TSIC.API. This class used to carry its
/// own copy, which accepted anything containing an '@'.
/// </summary>
public static class BatchEmailRecipientFilter
{
    /// <inheritdoc cref="EmailAddressRules.NotGiven"/>
    public const string MissingEmailSentinel = EmailAddressRules.NotGiven;

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

    /// <inheritdoc cref="EmailAddressRules.IsSendable"/>
    public static bool IsSendable(string? email) => EmailAddressRules.IsSendable(email);
}
