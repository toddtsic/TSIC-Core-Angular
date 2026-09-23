using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using TSIC.Contracts.Dtos.EmailTroubleshooter;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Implements the E-Mail Troubleshooter. Suppression lookups/removals use the SES v2 client;
/// the forced test send reuses <see cref="IEmailService"/> (sendInDevelopment: true) so the
/// existing branding, gating, and v1 transport are preserved. Addresses are processed one at a
/// time so every result is attributable to a single recipient.
///
/// Investigate runs three checks in a deliberate order - suppression, then recipient DNS, then the
/// test send - because each can settle the question without the next. The DNS step exists because
/// SES accepting a message proves only that it queued the API call; it reports the real outcome
/// hours later as a bounce. Reading that acceptance as "the sending side is healthy" is what made
/// this tool tell a director to check the spam folder of a domain that does not exist.
/// </summary>
public sealed class EmailTroubleshooterService : IEmailTroubleshooterService
{
    private const string SuppressionStatusNot = "NotSuppressed";
    private const string SuppressionStatusYes = "Suppressed";
    private const string SuppressionStatusUnknown = "Unknown";

    // Which side of the exchange the evidence points at. "Address" is the one no amount of
    // contacting anybody will fix: the domain does not accept mail, so there is no recipient.
    private const string SideSending = "Sending";
    private const string SideAddress = "Address";
    private const string SideRecipient = "Recipient";
    private const string SideInconclusive = "Inconclusive";

    private readonly IAmazonSimpleEmailServiceV2 _sesV2;
    private readonly IEmailService _email;
    private readonly IRecipientDomainService _domains;
    private readonly ILogger<EmailTroubleshooterService> _logger;

    public EmailTroubleshooterService(
        IAmazonSimpleEmailServiceV2 sesV2,
        IEmailService email,
        IRecipientDomainService domains,
        ILogger<EmailTroubleshooterService> logger)
    {
        _sesV2 = sesV2;
        _email = email;
        _domains = domains;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SuppressionEntryDto>> CheckSuppressionAsync(
        IReadOnlyList<string> emails, CancellationToken cancellationToken = default)
    {
        var results = new List<SuppressionEntryDto>();
        foreach (var email in Normalize(emails))
        {
            var (status, reason, lastUpdate) = await LookupSuppressionAsync(email, cancellationToken);
            results.Add(new SuppressionEntryDto
            {
                Email = email,
                Status = status,
                Reason = reason,
                LastUpdate = lastUpdate
            });
        }
        return results;
    }

    public async Task<IReadOnlyList<SuppressionRemoveResultDto>> RemoveSuppressionAsync(
        IReadOnlyList<string> emails, CancellationToken cancellationToken = default)
    {
        var results = new List<SuppressionRemoveResultDto>();
        foreach (var email in Normalize(emails))
        {
            try
            {
                await _sesV2.DeleteSuppressedDestinationAsync(
                    new DeleteSuppressedDestinationRequest { EmailAddress = email }, cancellationToken);
                results.Add(new SuppressionRemoveResultDto { Email = email, Removed = true });
            }
            catch (NotFoundException)
            {
                // Already absent — treat as success (idempotent removal).
                results.Add(new SuppressionRemoveResultDto { Email = email, Removed = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Suppression removal failed for {Email}", email);
                results.Add(new SuppressionRemoveResultDto { Email = email, Removed = false, Error = ex.Message });
            }
        }
        return results;
    }

    public async Task<IReadOnlyList<EmailInvestigateResultDto>> InvestigateAsync(
        IReadOnlyList<string> emails, CancellationToken cancellationToken = default)
    {
        var results = new List<EmailInvestigateResultDto>();
        foreach (var email in Normalize(emails))
        {
            var (status, reason, _) = await LookupSuppressionAsync(email, cancellationToken);

            // Suppressed addresses are a hard stop on our side — SES will not deliver. Don't bother sending.
            if (status == SuppressionStatusYes)
            {
                results.Add(new EmailInvestigateResultDto
                {
                    Email = email,
                    SuppressionStatus = status,
                    SuppressionReason = reason,
                    DomainStatus = RecipientDomainStatus.Unknown.ToString(),
                    SendAccepted = false,
                    Side = SideSending,
                    Conclusion =
                        $"This is on the SENDING side and is fixable here. Our email service (Amazon SES) is " +
                        $"withholding delivery because this address is on our suppression list" +
                        (string.IsNullOrWhiteSpace(reason) ? "" : $" (reason: {reason})") +
                        ", from a previous bounce or complaint. Remove it from the suppression list (Suppression " +
                        "List tab) - and resolve the original cause - before mail will reach this recipient."
                });
                continue;
            }

            // DNS is checked BEFORE the test send, not after. SES queues a message for a domain that
            // does not exist just as readily as for one that does, then retries for 14 hours and
            // bounces — so sending first would manufacture the very noise this tool exists to explain.
            var domain = await _domains.CheckAsync(email, cancellationToken);
            if (domain == RecipientDomainStatus.NoMailExchanger)
            {
                results.Add(new EmailInvestigateResultDto
                {
                    Email = email,
                    SuppressionStatus = status,
                    SuppressionReason = reason,
                    DomainStatus = domain.ToString(),
                    SendAccepted = false,
                    Side = SideAddress,
                    Conclusion =
                        "The ADDRESS is wrong - this is not a spam-filter problem, and there is nobody to " +
                        "contact. The domain after the '@' publishes no mail server and accepts no mail, so " +
                        "nothing sent to it can ever arrive. This is almost always a typo (\"a.com\" typed " +
                        "for \"aol.com\"). Correct the address on the registration. Until it is corrected, " +
                        "every send to it retries for 14 hours and then bounces."
                });
                continue;
            }

            bool sendAccepted;
            string? sendError = null;
            try
            {
                sendAccepted = await _email.SendAsync(BuildTestMessage(email), sendInDevelopment: true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Test send threw for {Email}", email);
                sendAccepted = false;
                sendError = ex.Message;
            }

            string side;
            string conclusion;
            if (sendAccepted)
            {
                side = SideRecipient;
                conclusion =
                    "Our email service (Amazon SES) accepted this message and this address is NOT blocked " +
                    "on our side" +
                    (domain == RecipientDomainStatus.Accepts
                        ? ", and the recipient's domain does publish a working mail server. "
                        : ". We could not reach DNS to confirm the recipient's domain, so that part is unverified. ") +
                    "Be aware that acceptance is not proof of delivery - SES reports the real outcome hours " +
                    "later, as a bounce. If the message never arrived and no bounce came back, it is being " +
                    "filtered or held on the RECIPIENT's end - usually a junk/spam folder, or a mail-gateway " +
                    "filter that quarantined it silently. The recipient (or their email/IT provider) should " +
                    "check spam and quarantine, and allowlist support@teamsportsinfo.com.";
            }
            else
            {
                side = SideInconclusive;
                conclusion =
                    "The test message could NOT be sent from our system (the email service returned a failure). " +
                    "This points to the SENDING side - check the email service configuration and the AWS " +
                    "credentials/region for this environment before drawing any conclusion about the recipient.";
            }

            results.Add(new EmailInvestigateResultDto
            {
                Email = email,
                SuppressionStatus = status,
                SuppressionReason = reason,
                DomainStatus = domain.ToString(),
                SendAccepted = sendAccepted,
                Side = side,
                Conclusion = conclusion,
                Error = sendError
            });
        }
        return results;
    }

    private async Task<(string Status, string? Reason, DateTime? LastUpdate)> LookupSuppressionAsync(
        string email, CancellationToken cancellationToken)
    {
        try
        {
            var resp = await _sesV2.GetSuppressedDestinationAsync(
                new GetSuppressedDestinationRequest { EmailAddress = email }, cancellationToken);
            var dest = resp.SuppressedDestination;
            return (SuppressionStatusYes, dest?.Reason?.Value, dest?.LastUpdateTime);
        }
        catch (NotFoundException)
        {
            return (SuppressionStatusNot, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Suppression lookup failed for {Email}", email);
            return (SuppressionStatusUnknown, ex.Message, null);
        }
    }

    /// <summary>
    /// Multipart — HTML *and* text — on purpose. It was text-only, which made the probe unrepresentative
    /// of every message this system actually sends: the sweep digest, registration confirmations and the
    /// ARB family notices are all HtmlBody. Plain text is the shape a spam filter or mail gateway is
    /// least likely to hold, so a text-only probe could report "nothing wrong on the sending side" while
    /// the HTML mail the customer is complaining about was being quarantined. A diagnostic that clears a
    /// path it did not test is worse than no diagnostic.
    ///
    /// Setting both makes BodyBuilder emit multipart/alternative, which is also what well-formed real
    /// mail looks like — so this exercises the same branch the digest does, not a simpler one.
    /// </summary>
    private static EmailMessageDto BuildTestMessage(string toAddress) => new()
    {
        FromName = "TEAMSPORTSINFO.COM",
        ToAddresses = new List<string> { toAddress },
        Subject = "TSIC Email Test",
        TextBody = "This is an automated email delivery test from TEAMSPORTSINFO.COM. No action is needed - please disregard.",
        HtmlBody = "<p>This is an automated email delivery test from <strong>TEAMSPORTSINFO.COM</strong>.</p>"
            + "<p>No action is needed - please disregard.</p>"
    };

    private static IEnumerable<string> Normalize(IReadOnlyList<string> emails) =>
        (emails ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
