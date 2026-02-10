using TSIC.Contracts.Dtos.RosterSwapper;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Roster Swapper admin tool.
/// Handles team/pool selection, roster loading, fee preview, and multi-flow transfers.
/// </summary>
public interface IRosterSwapperService
{
    Task<List<SwapperPoolOptionDto>> GetPoolOptionsAsync(Guid jobId, CancellationToken ct = default);

    Task<List<SwapperPlayerDto>> GetRosterAsync(Guid poolId, Guid jobId, CancellationToken ct = default);

    Task<List<RosterTransferFeePreviewDto>> PreviewTransferAsync(
        Guid jobId, RosterTransferPreviewRequest request, CancellationToken ct = default);

    Task<RosterTransferResultDto> ExecuteTransferAsync(
        Guid jobId, string adminUserId, RosterTransferRequest request, CancellationToken ct = default);

    Task TogglePlayerActiveAsync(Guid registrationId, Guid jobId, bool active, string adminUserId, CancellationToken ct = default);
}
