using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.TeamSearch;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Team Search admin tool.
/// Handles team search, detail view/edit, and all accounting operations
/// at the individual transaction, team, and cross-club levels.
/// </summary>
public interface ITeamSearchService
{
    // ── Search & filters ──

    Task<TeamSearchResponse> SearchAsync(Guid jobId, TeamSearchRequest request, CancellationToken ct = default);
    Task<TeamFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    // ── Team detail ──

    Task<TeamSearchDetailDto?> GetTeamDetailAsync(Guid teamId, Guid jobId, CancellationToken ct = default);
    Task EditTeamAsync(Guid teamId, Guid jobId, string userId, EditTeamRequest request, CancellationToken ct = default);

    // ── Individual transaction operations ──

    /// <summary>
    /// Refund a CC transaction. Checks ADN status: void if pending settlement, refund if settled.
    /// Creates negative payamt RegistrationAccounting record.
    /// </summary>
    Task<RefundResponse> ProcessRefundAsync(Guid jobId, string userId, RefundRequest request, CancellationToken ct = default);

    // ── Team-level payment operations ──

    /// <summary>
    /// Charge CC for a specific team's owed amount.
    /// </summary>
    Task<TeamCcChargeResponse> ChargeCcForTeamAsync(Guid jobId, string userId, TeamCcChargeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Record a check or correction for a specific team.
    /// </summary>
    Task<TeamCheckOrCorrectionResponse> RecordCheckForTeamAsync(Guid jobId, string userId, TeamCheckOrCorrectionRequest request, CancellationToken ct = default);

    // ── Club-level payment operations (cross-club spreading) ──

    /// <summary>
    /// Charge CC across all active club teams with OwedTotal > 0.
    /// Each team gets its own ADN charge call.
    /// </summary>
    Task<TeamCcChargeResponse> ChargeCcForClubAsync(Guid jobId, string userId, TeamCcChargeRequest request, CancellationToken ct = default);

    /// <summary>
    /// Record a check or correction spread across all active club teams.
    /// Distributes payment ordered by OwedTotal DESC with processing fee adjustments.
    /// </summary>
    Task<TeamCheckOrCorrectionResponse> RecordCheckForClubAsync(Guid jobId, string userId, TeamCheckOrCorrectionRequest request, CancellationToken ct = default);

    // ── Shared ──

    Task<List<PaymentMethodOptionDto>> GetPaymentMethodOptionsAsync(CancellationToken ct = default);
}
