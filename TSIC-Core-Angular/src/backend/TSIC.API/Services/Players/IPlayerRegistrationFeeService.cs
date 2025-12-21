using TSIC.Domain.Entities;

namespace TSIC.API.Services.Players;

/// <summary>
/// Handles fee resolution and initial fee application for player registrations.
/// Extracted from PlayerRegistrationService for single responsibility and testability.
/// </summary>
public interface IPlayerRegistrationFeeService
{
    /// <summary>
    /// Resolves the base fee for a team using centralized TeamLookupService with legacy fallbacks.
    /// </summary>
    Task<decimal> ResolveTeamBaseFeeAsync(Guid teamId);

    /// <summary>
    /// Applies initial fees to a registration record if not yet paid.
    /// Computes FeeBase, FeeProcessing, FeeTotal, and OwedTotal.
    /// </summary>
    Task ApplyInitialFeesAsync(Registrations reg, Guid teamId, decimal? teamFeeBase, decimal? teamPerRegistrantFee);
}
