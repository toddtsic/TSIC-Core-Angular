using TSIC.Contracts.Dtos.EmailTroubleshooter;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Account;

/// <summary>
/// Player-facing email deliverability self-service: Amazon SES suppression self-check/unsuppress,
/// a real test send, and this job's send history — all scoped to the family's own sendable emails
/// for the job (<see cref="IFamiliesRepository.GetEmailsForFamilyAndPlayersAsync"/>, which already
/// excludes null and the not@given.com sentinel). SES suppression/test-send are delegated to the
/// existing <see cref="IEmailTroubleshooterService"/>; the family set is the trust boundary, so an
/// unsuppress or a test send is refused for any address not in it.
/// </summary>
public sealed class MyEmailDeliverabilityService : IMyEmailDeliverabilityService
{
    private readonly IFamiliesRepository _families;
    private readonly IEmailTroubleshooterService _troubleshooter;
    private readonly IEmailLogRepository _emailLogs;

    public MyEmailDeliverabilityService(
        IFamiliesRepository families,
        IEmailTroubleshooterService troubleshooter,
        IEmailLogRepository emailLogs)
    {
        _families = families;
        _troubleshooter = troubleshooter;
        _emailLogs = emailLogs;
    }

    public async Task<IReadOnlyList<SuppressionEntryDto>> GetStatusAsync(
        Guid jobId, string familyUserId, CancellationToken cancellationToken = default)
    {
        var emails = await _families.GetEmailsForFamilyAndPlayersAsync(jobId, familyUserId, cancellationToken);
        if (emails.Count == 0)
        {
            return Array.Empty<SuppressionEntryDto>();
        }
        return await _troubleshooter.CheckSuppressionAsync(emails, cancellationToken);
    }

    public async Task<SuppressionRemoveResultDto?> UnsuppressAsync(
        Guid jobId, string familyUserId, string email, CancellationToken cancellationToken = default)
    {
        var target = await AuthorizeOwnAddressAsync(jobId, familyUserId, email, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var results = await _troubleshooter.RemoveSuppressionAsync(new[] { target }, cancellationToken);
        return results.Count > 0
            ? results[0]
            : new SuppressionRemoveResultDto { Email = target, Removed = false, Error = "No result returned." };
    }

    public async Task<EmailInvestigateResultDto?> TestSendAsync(
        Guid jobId, string familyUserId, string email, CancellationToken cancellationToken = default)
    {
        var target = await AuthorizeOwnAddressAsync(jobId, familyUserId, email, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var results = await _troubleshooter.InvestigateAsync(new[] { target }, cancellationToken);
        return results.Count > 0
            ? results[0]
            : new EmailInvestigateResultDto
            {
                Email = target,
                SuppressionStatus = "Unknown",
                SendAccepted = false,
                Side = "Inconclusive",
                Conclusion = "No result returned."
            };
    }

    public async Task<IReadOnlyList<PlayerSentEmailDto>> GetSentHistoryAsync(
        Guid jobId, string familyUserId, CancellationToken cancellationToken = default)
    {
        var emails = await _families.GetEmailsForFamilyAndPlayersAsync(jobId, familyUserId, cancellationToken);
        if (emails.Count == 0)
        {
            return Array.Empty<PlayerSentEmailDto>();
        }
        return await _emailLogs.GetSentToAddressesAsync(jobId, emails, cancellationToken);
    }

    /// <summary>
    /// Returns the trimmed address only if it belongs to this family's sendable set for the job;
    /// otherwise null. The single gate that authorizes touching the account-wide Amazon SES list /
    /// sending through our SES.
    /// </summary>
    private async Task<string?> AuthorizeOwnAddressAsync(
        Guid jobId, string familyUserId, string email, CancellationToken cancellationToken)
    {
        var target = email?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var family = await _families.GetEmailsForFamilyAndPlayersAsync(jobId, familyUserId, cancellationToken);
        return family.Any(e => string.Equals(e, target, StringComparison.OrdinalIgnoreCase))
            ? target
            : null;
    }
}
