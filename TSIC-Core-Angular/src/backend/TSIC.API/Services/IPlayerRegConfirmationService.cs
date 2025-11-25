using TSIC.API.Dtos;

namespace TSIC.API.Services;

public interface IPlayerRegConfirmationService
{
    Task<PlayerRegConfirmationDto> BuildAsync(Guid jobId, string familyUserId, CancellationToken ct);
    Task<(string Subject, string Html)> BuildEmailAsync(Guid jobId, string familyUserId, CancellationToken ct);
}
