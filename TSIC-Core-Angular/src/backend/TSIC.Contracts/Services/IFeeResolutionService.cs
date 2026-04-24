using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Services;

/// <summary>
/// Single source of truth for fee resolution and application.
/// Replaces PlayerRegistrationFeeService and TeamFeeCalculator.
///
/// Resolution: reads from fees.JobFees via cascade (Team → Agegroup → Job).
/// Application: stamps resolved fees onto Registration or Team snapshot fields.
///
/// Key rules:
///   - New registration: resolve base fee + evaluate active modifiers at DateTime.UtcNow
///   - Roster/division swap: resolve base fee only; modifiers are FROZEN from original registration
///   - Admin bulk recalc: resolve base fee only; modifiers stay frozen
/// </summary>
public interface IFeeResolutionService
{
    // ── Processing Fee Rate ─────────────────────────────────────

    /// <summary>
    /// Returns the effective processing fee rate as a decimal multiplier (e.g. 0.035 for 3.5%).
    /// Business rule: Math.Max(Jobs.ProcessingFeePercent ?? 3.5, 3.5) / 100.
    /// Floor of 3.5% — jobs can only override upward.
    /// </summary>
    Task<decimal> GetEffectiveProcessingRateAsync(Guid jobId, CancellationToken ct = default);

    // ── Resolution ──────────────────────────────────────────────

    /// <summary>
    /// Resolves the effective base fee for a role at a specific team.
    /// Cascade: Team → Agegroup → Job. Returns null if no fee configured.
    /// </summary>
    Task<ResolvedFee?> ResolveFeeAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective base fee at the agegroup level (no team override).
    /// Cascade: Agegroup → Job.
    /// </summary>
    Task<ResolvedFee?> ResolveFeeForAgegroupAsync(
        Guid jobId, string roleId, Guid agegroupId,
        CancellationToken ct = default);

    /// <summary>
    /// Batch: resolves fees for multiple teams in a single query.
    /// Used by LADT bulk recalculation.
    /// </summary>
    Task<Dictionary<Guid, ResolvedFee>> ResolveFeesByTeamIdsAsync(
        Guid jobId, string roleId, IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates active modifiers (discounts, late fees) at a point in time.
    /// Collects from all cascade levels (team, agegroup, job) and stacks them.
    /// Only called for NEW registrations — never on swap/recalc.
    /// </summary>
    Task<ResolvedModifiers> EvaluateModifiersAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        DateTime asOfDate,
        CancellationToken ct = default);

    // ── Resolution (Job-level, no agegroup/team) ─────────────────

    /// <summary>
    /// Resolves the job-level fee for adult roles (no agegroup/team context).
    /// Returns null if no fee configured.
    /// </summary>
    Task<ResolvedFee?> ResolveJobLevelFeeAsync(
        Guid jobId, string roleId,
        CancellationToken ct = default);

    // ── Application (Adult registrations) ───────────────────────

    /// <summary>
    /// Apply fees to an adult registration (job-level only, no agegroup/team).
    /// Resolves base fee + evaluates job-level modifiers at DateTime.UtcNow.
    /// Sets FeeBase, FeeDiscount, FeeLatefee, FeeProcessing, FeeTotal, OwedTotal.
    /// </summary>
    Task ApplyNewAdultRegistrationFeesAsync(
        Registrations reg, Guid jobId, string roleId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Apply fees to an adult registration that's assigned to a specific team
    /// (e.g. tournament Staff coaching a specific team). Uses the full
    /// Team → Agegroup → Job cascade for base fee + modifiers, so per-team
    /// pricing is respected. One call per (registration, team).
    /// </summary>
    Task ApplyNewStaffRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    // ── Application (Player registrations) ──────────────────────

    /// <summary>
    /// Apply fees to a player registration for a NEW registration.
    /// Resolves base fee + evaluates modifiers at DateTime.UtcNow.
    /// Sets FeeBase, FeeDiscount, FeeLatefee, FeeProcessing, FeeTotal, OwedTotal.
    /// </summary>
    Task ApplyNewRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Apply fees to a player registration after a roster swap.
    /// Only resolves new base fee from target team. Modifiers are PRESERVED.
    /// Recalculates FeeProcessing, FeeTotal, OwedTotal.
    /// </summary>
    Task ApplySwapFeesAsync(
        Registrations reg, Guid jobId, Guid targetAgegroupId, Guid targetTeamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    // ── Application (Team entities) ─────────────────────────────

    /// <summary>
    /// Apply fees to a team entity for a NEW team registration.
    /// Resolves ClubRep base fee + evaluates modifiers at DateTime.UtcNow.
    /// </summary>
    Task ApplyNewTeamFeesAsync(
        Domain.Entities.Teams team, Guid jobId, Guid agegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Apply fees to a team entity after a division swap.
    /// Only resolves new base fee from target agegroup. Modifiers are PRESERVED.
    /// </summary>
    Task ApplyTeamSwapFeesAsync(
        Domain.Entities.Teams team, Guid jobId, Guid targetAgegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default);
}

/// <summary>
/// Resolved modifiers from cascade evaluation.
/// Discounts and late fees are summed across all cascade levels.
/// </summary>
public record ResolvedModifiers
{
    public decimal TotalDiscount { get; init; }
    public decimal TotalLateFee { get; init; }
}

/// <summary>
/// Context for player fee application — controls processing fee behavior.
/// </summary>
public record FeeApplicationContext
{
    /// <summary>Whether to apply CC processing fees (from job BAddProcessingFees flag).</summary>
    public bool AddProcessingFees { get; init; } = true;

    /// <summary>Sum of non-credit-card payments for processing fee basis adjustment.</summary>
    public decimal NonCcPayments { get; init; }

    /// <summary>
    /// True when the player is paying in full (PIF) — FeeBase = Deposit + BalanceDue.
    /// False (default) = deposit-only phase; FeeBase = Deposit when configured, else BalanceDue.
    /// Controlled by |ALLOWPIF in Jobs.CoreRegformPlayer + player's checkout choice.
    /// </summary>
    public bool IsFullPayment { get; init; }
}

/// <summary>
/// Context for team fee application — controls phase and processing fee behavior.
/// </summary>
public record TeamFeeApplicationContext
{
    /// <summary>Whether this is the balance-due phase (true) or deposit phase (false).</summary>
    public bool IsFullPaymentRequired { get; init; }

    /// <summary>Whether to apply CC processing fees.</summary>
    public bool AddProcessingFees { get; init; } = true;

    /// <summary>Whether processing fees apply to the full amount or team fee only.</summary>
    public bool ApplyProcessingFeesToDeposit { get; init; }

    /// <summary>
    /// Effective processing fee rate as a decimal multiplier (e.g. 0.035 for 3.5%).
    /// Resolved by caller via IFeeResolutionService.GetEffectiveProcessingRateAsync.
    /// </summary>
    public required decimal ProcessingFeePercent { get; init; }
}
