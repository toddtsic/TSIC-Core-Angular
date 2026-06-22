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

    /// <summary>Director approval queue: unassigned coaches with pending team requests + recognition context.</summary>
    Task<List<UnassignedAdultQueueRowDto>> GetUnassignedAdultQueueAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Approve one requested team: mints the per-team Staff row via the FLOW-2 transfer
    /// path. The UnassignedAdult row remains as the source of further acceptances.
    /// </summary>
    Task<RosterTransferResultDto> ApproveTeamRequestAsync(
        Guid jobId, string adminUserId, Guid registrationId, Guid teamId, CancellationToken ct = default);

    /// <summary>Deny one requested team: drop it from the coach's codified requests (no Staff row).</summary>
    Task<bool> DenyTeamRequestAsync(
        Guid jobId, string adminUserId, Guid registrationId, Guid teamId, CancellationToken ct = default);
}
