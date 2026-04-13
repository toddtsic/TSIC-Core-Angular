using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Services;

/// <summary>
/// Single source of truth for fee resolution and application.
/// Reads from <c>fees.JobFees</c>, stamps results onto Registration/Team snapshot fields.
/// Replaces PlayerRegistrationFeeService and TeamFeeCalculator.
///
/// <para>
/// <b>Cascade rules, by role</b> — enforced by the method signatures below (a caller
/// cannot ask for an invalid scope):
/// </para>
/// <list type="bullet">
///   <item><b>Player</b>: Team → Agegroup → Job. Team-level overrides are honored.</item>
///   <item><b>Staff</b> (adult coaching a specific team): Team → Agegroup → Job. Team-level overrides are honored.</item>
///   <item><b>ClubRep</b>: Agegroup → Job only. No team-level override — fee per team
///     registration is determined by the agegroup it's in. Enforced at the domain layer.</item>
///   <item><b>Other adult roles</b> (Director, Referee, Recruiter, UA, …): Job-level only.</item>
/// </list>
///
/// <para>
/// <b>Modifier rules</b>: modifier evaluation mirrors the same scope rules as fee resolution.
/// <c>EvaluateNew*</c> methods are used on new registrations. Swap/recalc paths preserve
/// existing modifiers and only re-resolve the base fee.
/// </para>
/// </summary>
public interface IFeeResolutionService
{
    // ── Processing Fee Rate ─────────────────────────────────────

    /// <summary>
    /// Returns the effective processing fee rate as a decimal multiplier (e.g. 0.035 for 3.5%).
    /// Business rule: <c>Math.Max(Jobs.ProcessingFeePercent ?? 3.5, 3.5) / 100</c>.
    /// Floor of 3.5% — jobs can only override upward.
    /// </summary>
    Task<decimal> GetEffectiveProcessingRateAsync(Guid jobId, CancellationToken ct = default);

    // ── Resolution: team-scoped roles ───────────────────────────

    /// <summary>
    /// Resolves the effective Player base fee for a specific team.
    /// Cascade: Team → Agegroup → Job. Returns null if no fee configured.
    /// </summary>
    Task<ResolvedFee?> ResolvePlayerFeeAsync(
        Guid jobId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective Staff base fee for a specific team (adults coaching a team).
    /// Cascade: Team → Agegroup → Job. Returns null if no fee configured.
    /// </summary>
    Task<ResolvedFee?> ResolveStaffFeeAsync(
        Guid jobId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default);

    /// <summary>
    /// Batch: resolves Player fees for multiple teams in a single query.
    /// Used by LADT bulk recalculation and team-picker displays.
    /// </summary>
    Task<Dictionary<Guid, ResolvedFee>> ResolvePlayerFeesByTeamIdsAsync(
        Guid jobId, IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default);

    // ── Resolution: agegroup-scoped role (ClubRep) ──────────────

    /// <summary>
    /// Resolves the effective ClubRep base fee for an agegroup.
    /// Cascade: Agegroup → Job. <b>Team-level override is not permitted</b> —
    /// the per-team registration fee is determined by the agegroup the team sits in.
    /// </summary>
    Task<ResolvedFee?> ResolveClubRepFeeAsync(
        Guid jobId, Guid agegroupId,
        CancellationToken ct = default);

    // ── Resolution: job-scoped adult roles ──────────────────────

    /// <summary>
    /// Resolves the job-level fee for an adult role with no agegroup/team context
    /// (Director, Referee, Recruiter, UA, etc.). Returns null if none configured.
    /// </summary>
    Task<ResolvedFee?> ResolveAdultJobFeeAsync(
        Guid jobId, string roleId,
        CancellationToken ct = default);

    // ── Modifier evaluation ─────────────────────────────────────

    /// <summary>
    /// Evaluates active Player modifiers (discounts, late fees) at a point in time.
    /// Collects from Team + Agegroup + Job levels. Only called for new registrations.
    /// </summary>
    Task<ResolvedModifiers> EvaluatePlayerModifiersAsync(
        Guid jobId, Guid agegroupId, Guid teamId, DateTime asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates active Staff modifiers (discounts, late fees) at a point in time.
    /// Collects from Team + Agegroup + Job levels. Only called for new registrations.
    /// </summary>
    Task<ResolvedModifiers> EvaluateStaffModifiersAsync(
        Guid jobId, Guid agegroupId, Guid teamId, DateTime asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates active ClubRep modifiers at a point in time.
    /// Collects from Agegroup + Job levels (no team scope by domain rule).
    /// Only called for new team registrations.
    /// </summary>
    Task<ResolvedModifiers> EvaluateClubRepModifiersAsync(
        Guid jobId, Guid agegroupId, DateTime asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates active adult job-level modifiers for a role (no agegroup/team).
    /// Used by adult registrations that don't attach to a team.
    /// </summary>
    Task<ResolvedModifiers> EvaluateAdultJobModifiersAsync(
        Guid jobId, string roleId, DateTime asOfDate,
        CancellationToken ct = default);

    // ── Application (Adult registrations) ───────────────────────

    /// <summary>
    /// Apply fees to an adult registration (job-level only, no agegroup/team).
    /// Resolves base fee + evaluates job-level modifiers at DateTime.UtcNow.
    /// </summary>
    Task ApplyNewAdultRegistrationFeesAsync(
        Registrations reg, Guid jobId, string roleId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Apply fees to an adult Staff registration assigned to a specific team
    /// (e.g. tournament Staff coaching a specific team). Uses Team → Agegroup → Job
    /// cascade so per-team pricing is respected. One call per (registration, team).
    /// </summary>
    Task ApplyNewStaffRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    // ── Application (Player registrations) ──────────────────────

    /// <summary>
    /// Apply fees to a player registration for a NEW registration.
    /// Resolves base fee + evaluates modifiers at DateTime.UtcNow.
    /// </summary>
    Task ApplyNewRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Apply fees to a player registration after a roster swap.
    /// Only resolves new base fee from target team. Modifiers are PRESERVED.
    /// </summary>
    Task ApplySwapFeesAsync(
        Registrations reg, Guid jobId, Guid targetAgegroupId, Guid targetTeamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default);

    // ── Application (Team entities) ─────────────────────────────

    /// <summary>
    /// Apply fees to a team entity for a NEW team registration.
    /// Resolves ClubRep base fee (agegroup-scoped) + evaluates modifiers at DateTime.UtcNow.
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
