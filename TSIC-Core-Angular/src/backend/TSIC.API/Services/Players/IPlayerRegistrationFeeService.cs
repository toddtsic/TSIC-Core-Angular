using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Players;

/// <summary>
/// Single source of truth for player fee resolution and application.
/// All code paths that determine or write player fees MUST go through this service.
/// </summary>
public interface IPlayerRegistrationFeeService
{
    /// <summary>
    /// Pure fee cascade — no DB call, testable in isolation.
    /// Cascade (most specific → most general):
    ///   Team.PerRegistrantFee → Agegroup.PlayerFeeOverride → League.PlayerFeeOverride → 0.
    /// AG.TeamFee and AG.RosterFee are club rep fees and are NOT in this cascade.
    /// Tournament guard: If JobTypeId == 2 and no explicit player fee at any level, returns 0.
    /// </summary>
    decimal ResolveBaseFee(TeamFeeData data);

    /// <summary>
    /// Single team — fetches TeamFeeData from repo, then applies cascade.
    /// </summary>
    Task<decimal> ResolveBaseFeeAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Batch — single DB query for bulk scenarios (LADT agegroup recalc).
    /// Returns resolved base fee per teamId.
    /// </summary>
    Task<Dictionary<Guid, decimal>> ResolveBaseFeesByTeamIdsAsync(
        IReadOnlyList<Guid> teamIds, CancellationToken ct = default);

    /// <summary>
    /// Apply/recalculate fees on a registration entity.
    /// When isRecalculation=false (wizard): only sets FeeBase if current FeeBase ≤ 0.
    /// When isRecalculation=true (swap/bulk): always overwrites FeeBase.
    /// When baseFee=0: zeros FeeBase, FeeProcessing, FeeDiscount, FeeLatefee; preserves FeeDonation.
    /// When baseFee>0: preserves FeeDiscount, FeeDonation, FeeLatefee.
    /// Always recalculates FeeTotal and OwedTotal.
    /// </summary>
    void ApplyFees(Registrations reg, decimal baseFee, PlayerFeeContext ctx);
}

/// <summary>
/// Context for fee application — controls recalculation behavior.
/// </summary>
public record PlayerFeeContext
{
    /// <summary>true = always overwrite FeeBase; false = only set if FeeBase ≤ 0.</summary>
    public bool IsRecalculation { get; init; }

    /// <summary>Whether to apply CC processing fees (from job BAddProcessingFees flag).</summary>
    public bool AddProcessingFees { get; init; } = true;

    /// <summary>Sum of non-credit-card payments for processing fee discount calculation.</summary>
    public decimal NonCcPayments { get; init; }
}
