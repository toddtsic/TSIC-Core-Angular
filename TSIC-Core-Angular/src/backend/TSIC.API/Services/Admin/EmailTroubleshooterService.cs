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
/// </summary>
public sealed class EmailTroubleshooterService : IEmailTroubleshooterService
{
    private const string SuppressionStatusNot = "NotSuppressed";
    private const string SuppressionStatusYes = "Suppressed";
    private const string SuppressionStatusUnknown = "Unknown";

    private readonly IAmazonSimpleEmailServiceV2 _sesV2;
    private readonly IEmailService _email;
    private readonly ILogger<EmailTroubleshooterService> _logger;

    public EmailTroubleshooterService(
        IAmazonSimpleEmailServiceV2 sesV2,
        IEmailService email,
        ILogger<EmailTroubleshooterService> logger)
    {
        _sesV2 = sesV2;
        _email = email;
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
                    SendAccepted = false,
                    Side = "Sending",
                    Conclusion =
                        $"This is on the SENDING side and is fixable here. Our email service (Amazon SES) is " +
                        $"withholding delivery because this address is on our suppression list" +
                        (string.IsNullOrWhiteSpace(reason) ? "" : $" (reason: {reason})") +
                        ", from a previous bounce or complaint. Remove it from the suppression list (Suppression " +
                        "List tab) - and resolve the original cause - before mail will reach this recipient."
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
                side = "Recipient";
                conclusion =
                    "The message left TEAMSPORTSINFO.COM successfully. Our email service (Amazon SES) confirmed " +
                    "this address is NOT blocked on our side and accepted the message for delivery. There is " +
                    "nothing wrong on the sending side. If the message was not received, it is being filtered or " +
                    "held on the RECIPIENT's end - almost always a junk/spam folder, or a mail-gateway/security " +
                    "filter that quarantined it silently. The recipient (or their email/IT provider) should check " +
                    "spam and quarantine, and allowlist support@teamsportsinfo.com.";
            }
            else
            {
                side = "Inconclusive";
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

    private static EmailMessageDto BuildTestMessage(string toAddress) => new()
    {
        FromName = "TEAMSPORTSINFO.COM",
        FromAddress = TsicConstants.SupportEmail,
        ToAddresses = new List<string> { toAddress },
        Subject = "TSIC Email Test",
        TextBody = "This is an automated email delivery test from TEAMSPORTSINFO.COM. No action is needed - please disregard."
    };

    private static IEnumerable<string> Normalize(IReadOnlyList<string> emails) =>
        (emails ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
