namespace TSIC.Contracts.Services;

/// <summary>
/// Whether a recipient domain is able to receive mail at all, per DNS.
/// </summary>
public enum RecipientDomainStatus
{
    /// <summary>The domain publishes an MX record, or an A record that acts as an implicit MX.</summary>
    Accepts,

    /// <summary>The domain resolved cleanly and has neither an MX nor an A record, or does not exist.</summary>
    NoMailExchanger,

    /// <summary>DNS could not answer — timeout, SERVFAIL, blocked port 53. Never treated as a failure.</summary>
    Unknown
}

/// <summary>
/// Answers the one question Amazon SES cannot: can the recipient's domain receive mail at all.
///
/// SES accepting a message means only that it queued the API call — it reports the real outcome
/// hours later as an asynchronous bounce. So a send to a domain that does not exist looks like a
/// success at the call site and fails 14 hours later. The E-Mail Troubleshooter used to read that
/// acceptance as proof the sending side was healthy and told the user to go check their spam
/// folder, for a mailbox that could never exist.
///
/// This is diagnostic only. It is deliberately NOT wired into the send path: a DNS round trip per
/// recipient on a 500-address batch is a different decision with a different cost.
/// </summary>
public interface IRecipientDomainService
{
    /// <summary>
    /// Resolves the domain part of <paramref name="email"/>. Never throws and never guesses:
    /// anything other than a clean answer comes back <see cref="RecipientDomainStatus.Unknown"/>,
    /// so a DNS outage can never condemn a working address.
    /// </summary>
    Task<RecipientDomainStatus> CheckAsync(string email, CancellationToken cancellationToken = default);
}
