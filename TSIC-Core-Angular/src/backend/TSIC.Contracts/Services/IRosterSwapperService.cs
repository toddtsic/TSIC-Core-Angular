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

    /// <summary>Director approval queue: every active coach with their tagged team record +
    /// live grants + recognition context. Seeds pre-existing grants into the record first.</summary>
    Task<List<UnassignedAdultQueueRowDto>> GetUnassignedAdultQueueAsync(
        Guid jobId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Approve (grant) one team: mints the per-team Staff row via the FLOW-2 transfer path and
    /// appends the team to the coach's record as admin. The UnassignedAdult row remains.
    /// </summary>
    Task<RosterTransferResultDto> ApproveTeamRequestAsync(
        Guid jobId, string adminUserId, Guid registrationId, Guid teamId, CancellationToken ct = default);

    /// <summary>Deny a coach outright: delete ALL their Staff rows + deactivate the anchor
    /// (bActive=0). The immutable team record is left untouched.</summary>
    Task<bool> DenyCoachAsync(
        Guid jobId, string adminUserId, Guid registrationId, CancellationToken ct = default);

    /// <summary>Re-validate a coach's USLax membership currency against USA Lacrosse now, refresh
    /// the stored expiry on the anchor + all their Staff rows in the job, and return current
    /// status/expiry. Identity verification is unaffected — currency only.</summary>
    Task<RevalidateUsLaxResultDto> RevalidateUsLaxAsync(
        Guid jobId, Guid registrationId, CancellationToken ct = default);
}
