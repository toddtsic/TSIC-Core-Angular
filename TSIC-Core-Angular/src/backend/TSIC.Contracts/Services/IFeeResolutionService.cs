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
    /// Returns the effective CC processing fee rate as a decimal multiplier (e.g. 0.035 for 3.5%).
    /// Business rule: Math.Clamp(Jobs.ProcessingFeePercent ?? 3.5, 3.5, 4.0) / 100.
    /// Floor of 3.5% (jobs can only override upward), ceiling of 4.0% (typo guard).
    /// </summary>
    Task<decimal> GetEffectiveProcessingRateAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Returns the effective eCheck processing fee rate as a decimal multiplier (e.g. 0.015 for 1.5%).
    /// Business rule: Math.Clamp(Jobs.EcprocessingFeePercent ?? 1.5, 1.5, 2.0) / 100.
    /// Floor of 1.5%, ceiling of 2.0%.
    /// </summary>
    Task<decimal> GetEffectiveEcheckProcessingRateAsync(Guid jobId, CancellationToken ct = default);

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

    /// <summary>
    /// Upgrades a registration from deposit phase to Pay In Full.
    /// Re-stamps FeeBase = Deposit + BalanceDue and recomputes FeeProcessing
    /// on the new base (proportional net-billable rule). Modifiers are PRESERVED.
    /// Caller MUST verify the job has ALLOWPIF before invoking — this method
    /// does not re-check that policy gate.
    /// </summary>
    Task ApplyPifUpgradeAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Re-levies a registration's processing fee + totals from its CURRENT snapshot fields
    /// (FeeBase / FeeDiscount / FeeLatefee / FeeDonation) and payment history, WITHOUT
    /// re-resolving the base fee. Use when a modifier already stamped on the row changes the
    /// derived money — e.g. a donation added at payment time on the deposit path, where the
    /// PIF recompute (<see cref="ApplyPifUpgradeAsync"/>) does not run. FeeProcessing is reset
    /// from PaymentState.FeeProcessingTarget (not incremented), so repeat calls are idempotent.
    /// </summary>
    Task RecomputeRegistrationFinancialsAsync(
        Registrations reg, Guid jobId, CancellationToken ct = default);

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
/// Context for player fee application — controls phase and processing fee behavior.
/// IsFullPaymentRequired is the job-level BASELINE (Jobs.BPlayersFullPaymentRequired);
/// FeeResolutionService overrides it per scope via ResolvedFee.ResolveFullPaymentPhase
/// (a team/agegroup/league JobFees override wins). Effective full-payment → FeeBase =
/// Deposit + BalanceDue; effective deposit phase → FeeBase = Deposit (or BalanceDue when
/// no deposit configured). ApplyPifUpgradeAsync remains the per-registration "parent
/// voluntarily pays in full at checkout" path.
///
/// NonCcPayments is NOT in this context — FeeResolutionService looks it up from the
/// registration's payment history when stamping FeeProcessing.
/// </summary>
public record FeeApplicationContext
{
    /// <summary>
    /// Job-level phase BASELINE: true = full-payment, false = deposit. Defaults to false.
    /// Callers populate from Jobs.BPlayersFullPaymentRequired; the service treats this as
    /// the FALLBACK — a per-scope JobFees override (BFullPaymentRequired) takes precedence.
    /// </summary>
    public bool IsFullPaymentRequired { get; init; }

    /// <summary>Whether to apply CC processing fees (from job BAddProcessingFees flag).</summary>
    public bool AddProcessingFees { get; init; } = true;

    /// <summary>
    /// Reprice-only: when true, the swap applier may RETROACTIVELY stamp a currently-active late
    /// fee onto a registration that carries NONE yet AND still owes principal against the full
    /// price. Lets a director who adds/raises a late fee reach registrants who signed up before
    /// the late window. Discount/donation stay frozen — this only ever ADDS a late fee where none
    /// exists, never doubles one or strips a discount. Default false: roster-swap/club-roster/seat
    /// /waitlist callers keep all modifiers frozen.
    /// </summary>
    public bool AssessActiveLateFee { get; init; }
}

/// <summary>
/// Context for team fee application — controls phase and processing fee behavior.
///
/// NonCcPayments is NOT in this context — FeeResolutionService looks it up from the
/// team's payment history when stamping FeeProcessing.
/// </summary>
public record TeamFeeApplicationContext
{
    /// <summary>
    /// Job-level phase BASELINE: true = full-payment (balance-due) phase, false = deposit.
    /// Callers populate from Jobs.BTeamsFullPaymentRequired; the service treats this as the
    /// FALLBACK — a per-scope JobFees override (BFullPaymentRequired) takes precedence via
    /// ResolvedFee.ResolveFullPaymentPhase.
    /// </summary>
    public bool IsFullPaymentRequired { get; init; }

    /// <summary>Whether to apply CC processing fees.</summary>
    public bool AddProcessingFees { get; init; } = true;

    /// <summary>Whether processing fees apply to the full amount or team fee only.</summary>
    public bool ApplyProcessingFeesToDeposit { get; init; }

    /// <summary>
    /// Reprice-only: when true, the team swap applier may RETROACTIVELY stamp a currently-active
    /// late fee onto a team that carries NONE yet AND still owes principal against the full price.
    /// Lets a director who adds/raises a late fee reach teams that registered before the late
    /// window. Discount/donation stay frozen — this only ever ADDS a late fee where none exists,
    /// never doubles one. Default false: division-swap/pool-assignment callers keep modifiers frozen.
    /// </summary>
    public bool AssessActiveLateFee { get; init; }

    /// <summary>
    /// Effective processing fee rate as a decimal multiplier (e.g. 0.035 for 3.5%).
    /// Resolved by caller via IFeeResolutionService.GetEffectiveProcessingRateAsync.
    /// </summary>
    public required decimal ProcessingFeePercent { get; init; }
}
