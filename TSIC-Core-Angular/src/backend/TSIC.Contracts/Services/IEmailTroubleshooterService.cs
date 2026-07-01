using TSIC.Contracts.Dtos.EmailTroubleshooter;

namespace TSIC.Contracts.Services;

/// <summary>
/// Admin diagnostics for "the client says our email never arrived" tickets.
/// Wraps the Amazon SES account-level suppression list (v2 API) and a forced test send
/// (reusing <see cref="IEmailService"/> with sendInDevelopment) to determine which side
/// of the exchange a delivery failure is on. Each address is processed independently.
/// </summary>
public interface IEmailTroubleshooterService
{
    /// <summary>Look up each address on the SES suppression list.</summary>
    Task<IReadOnlyList<SuppressionEntryDto>> CheckSuppressionAsync(
        IReadOnlyList<string> emails, CancellationToken cancellationToken = default);

    /// <summary>Remove each address from the SES suppression list (account-wide).</summary>
    Task<IReadOnlyList<SuppressionRemoveResultDto>> RemoveSuppressionAsync(
        IReadOnlyList<string> emails, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per address: check suppression, force a real test send, and conclude which side
    /// (sending vs recipient) a failure would be on.
    /// </summary>
    Task<IReadOnlyList<EmailInvestigateResultDto>> InvestigateAsync(
        IReadOnlyList<string> emails, CancellationToken cancellationToken = default);
}
