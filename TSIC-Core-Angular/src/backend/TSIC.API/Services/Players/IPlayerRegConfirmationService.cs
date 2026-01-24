using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Players;

public interface IPlayerRegConfirmationService
{
    Task<PlayerRegConfirmationDto> BuildAsync(Guid jobId, string familyUserId, CancellationToken ct);
    Task<PlayerRegConfirmationDto> BuildAsync(string jobPath, string familyUserId, CancellationToken ct);
    Task<(string Subject, string Html)> BuildEmailAsync(Guid jobId, string familyUserId, CancellationToken ct);
    Task<(string Subject, string Html)> BuildEmailAsync(string jobPath, string familyUserId, CancellationToken ct);
}
