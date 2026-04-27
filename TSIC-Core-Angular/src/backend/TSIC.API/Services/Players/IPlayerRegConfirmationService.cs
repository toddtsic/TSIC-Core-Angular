using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Players;

public interface IPlayerRegConfirmationService
{
    Task<PlayerRegConfirmationDto> BuildAsync(Guid jobId, string familyUserId, CancellationToken ct);
    Task<PlayerRegConfirmationDto> BuildAsync(string jobPath, string familyUserId, CancellationToken ct);
    /// <summary>
    /// Builds the registration confirmation email body. When <paramref name="isEcheckPending"/>
    /// is true, prepends an inline-styled "settlement pending" banner so customers know their
    /// registration is held until the bank confirms the debit (typically 3–5 business days).
    /// </summary>
    Task<(string Subject, string Html)> BuildEmailAsync(Guid jobId, string familyUserId, CancellationToken ct, bool isEcheckPending = false);
    Task<(string Subject, string Html)> BuildEmailAsync(string jobPath, string familyUserId, CancellationToken ct, bool isEcheckPending = false);
}
