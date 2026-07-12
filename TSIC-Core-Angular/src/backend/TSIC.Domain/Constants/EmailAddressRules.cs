using System.Diagnostics.CodeAnalysis;

namespace TSIC.Domain.Constants;

/// <summary>
/// The one rule for whether an address may be handed to SES.
///
/// It lives in Domain because both TSIC.API (the batch engine) and TSIC.Infrastructure (the schedule
/// and family recipient repositories) have to reach it, and Infrastructure cannot see API.
///
/// It exists because there were SIX divergent checks — four flavours of <c>Contains('@')</c> plus
/// <c>EmailAddressAttribute</c>, which only asserts a single non-terminal '@'. Every one of them
/// accepted <c>foo@gmail</c> and <c>na@na</c>, so 131 undeliverable addresses were being handed to SES
/// on every batch send and hard-bouncing. Hard bounces cost sender reputation and land recipients on
/// the suppression list, which then silently drops mail to people who DO have a valid address.
///
/// Add new send paths through here. Do not write another <c>Contains('@')</c>.
/// </summary>
public static class EmailAddressRules
{
    /// <summary>
    /// The recorded fact that a person has no email address — distinct from a blank, which says only
    /// that we never captured one. It is a well-formed address, so <see cref="IsDeliverable"/> accepts
    /// it and it must be excluded by name.
    /// </summary>
    public const string NotGiven = "not@given.com";

    /// <summary>True if this address is well-formed enough that SES could plausibly deliver it.</summary>
    public static bool IsWellFormed([NotNullWhen(true)] string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var value = email.Trim();
        if (value.Any(char.IsWhiteSpace)) return false;

        // Exactly one '@', with something either side.
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1) return false;

        var domain = value[(at + 1)..];

        // The check every one of the old rules was missing: a domain needs a dot and a TLD.
        // `foo@gmail`, `na@na` and `not@given` all die here.
        var dot = domain.LastIndexOf('.');
        if (dot <= 0 || dot == domain.Length - 1) return false;

        // A TLD is at least two characters and is not numeric ('foo@bar.1' is not a hostname).
        var tld = domain[(dot + 1)..];
        return tld.Length >= 2 && tld.All(char.IsLetter);
    }

    /// <summary>
    /// True if we should actually mail this address: well-formed, and not the "no email" marker.
    /// This is the predicate every send path wants.
    /// </summary>
    public static bool IsSendable([NotNullWhen(true)] string? email)
        => IsWellFormed(email)
           && !string.Equals(email.Trim(), NotGiven, StringComparison.OrdinalIgnoreCase);
}
