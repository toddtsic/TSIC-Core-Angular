using TSIC.Contracts.Dtos.PoolAssignment;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Pool Assignment admin tool.
/// Handles division selection, team listing, fee preview, and team transfers
/// between divisions (with schedule-aware symmetrical swap support).
/// </summary>
public interface IPoolAssignmentService
{
    Task<List<PoolDivisionOptionDto>> GetDivisionOptionsAsync(Guid jobId, CancellationToken ct = default);

    Task<List<PoolTeamDto>> GetTeamsAsync(Guid divId, Guid jobId, CancellationToken ct = default);

    Task<PoolTransferPreviewResponse> PreviewTransferAsync(
        Guid jobId, PoolTransferPreviewRequest request, CancellationToken ct = default);

    Task<PoolTransferResultDto> ExecuteTransferAsync(
        Guid jobId, string adminUserId, PoolTransferRequest request, CancellationToken ct = default);

    Task ToggleTeamActiveAsync(Guid teamId, Guid jobId, bool active, string adminUserId, CancellationToken ct = default);

    Task UpdateTeamDivRankAsync(Guid teamId, Guid jobId, int divRank, string adminUserId, CancellationToken ct = default);
}
