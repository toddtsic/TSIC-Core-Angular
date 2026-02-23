using TSIC.Contracts.Dtos.Scoring;

namespace TSIC.Contracts.Services;

public interface IMobileScorerService
{
    Task<List<MobileScorerDto>> GetScorersAsync(Guid jobId, CancellationToken ct = default);
    Task<MobileScorerDto> CreateScorerAsync(Guid jobId, CreateMobileScorerRequest request, string currentUserId, CancellationToken ct = default);
    Task UpdateScorerAsync(Guid registrationId, UpdateMobileScorerRequest request, string currentUserId, CancellationToken ct = default);
    Task DeleteScorerAsync(Guid registrationId, CancellationToken ct = default);
}
