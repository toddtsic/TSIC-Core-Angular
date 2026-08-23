using TSIC.Contracts.Dtos.Stp;

namespace TSIC.Contracts.Services;

/// <summary>
/// Stay-to-Play admin surface. Ports the one legacy screen that carried real
/// function (Controllers/STP/Admin/STPClubRepsController) — the club rep summary
/// a housing vendor sizes room blocks from.
///
/// Deliberately NOT ported: the legacy batch-email half (Todd 2026-08-23 —
/// Stay-to-Play is a data transfer to a third party, not a service we run for them)
/// and STPAdminAdd (the Administrators page already grants the role, and legacy's
/// add path set the new account's password equal to its username).
/// </summary>
public interface IStpService
{
    /// <summary>Active club reps on one job with their team counts.</summary>
    Task<List<StpClubRepDto>> GetClubRepsAsync(Guid jobId, CancellationToken cancellationToken = default);
}
