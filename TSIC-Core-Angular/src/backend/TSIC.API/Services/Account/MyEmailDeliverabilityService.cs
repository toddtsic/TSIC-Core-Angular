using TSIC.Contracts.Dtos.EmailTroubleshooter;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Account;

/// <summary>
/// Player-facing email deliverability self-service: suppression self-check/unsuppress, a real
/// test send, and own send history — all scoped to the family's own sendable emails
/// (<see cref="IFamiliesRepository.GetAllSendableEmailsForFamilyAsync"/>, which already excludes
/// null and the not@given.com sentinel). SES suppression/test-send are delegated to the existing
/// <see cref="IEmailTroubleshooterService"/>; the family set is the trust boundary, so an
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
        string familyUserId, CancellationToken cancellationToken = default)
    {
        var emails = await _families.GetAllSendableEmailsForFamilyAsync(familyUserId, cancellationToken);
        if (emails.Count == 0)
        {
            return Array.Empty<SuppressionEntryDto>();
        }
        return await _troubleshooter.CheckSuppressionAsync(emails, cancellationToken);
    }

    public async Task<SuppressionRemoveResultDto?> UnsuppressAsync(
        string familyUserId, string email, CancellationToken cancellationToken = default)
    {
        var target = await AuthorizeOwnAddressAsync(familyUserId, email, cancellationToken);
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
        string familyUserId, string email, CancellationToken cancellationToken = default)
    {
        var target = await AuthorizeOwnAddressAsync(familyUserId, email, cancellationToken);
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
        string familyUserId, CancellationToken cancellationToken = default)
    {
        var emails = await _families.GetAllSendableEmailsForFamilyAsync(familyUserId, cancellationToken);
        if (emails.Count == 0)
        {
            return Array.Empty<PlayerSentEmailDto>();
        }
        return await _emailLogs.GetSentToAddressesAllJobsAsync(emails, cancellationToken);
    }

    /// <summary>
    /// Returns the trimmed address only if it belongs to this family's sendable set; otherwise null.
    /// The single gate that authorizes touching the account-wide SES list / sending through our SES.
    /// </summary>
    private async Task<string?> AuthorizeOwnAddressAsync(
        string familyUserId, string email, CancellationToken cancellationToken)
    {
        var target = email?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var family = await _families.GetAllSendableEmailsForFamilyAsync(familyUserId, cancellationToken);
        return family.Any(e => string.Equals(e, target, StringComparison.OrdinalIgnoreCase))
            ? target
            : null;
    }
}
