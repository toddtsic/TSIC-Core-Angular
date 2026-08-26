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
    /// <summary>
    /// Resolves the fee a team WOULD price at once moved into <paramref name="targetAgegroupId"/> —
    /// team tier matched on TeamId alone (its row is about to be repointed), agegroup/league tiers
    /// read from the target. Used by the pool-transfer preview so the number the director approves
    /// is the number the move produces. See
    /// <see cref="RepointTeamScopedFeesAsync"/> for the invariant this anticipates.
    /// </summary>
    Task<ResolvedFee?> ResolveFeeForTeamAtAgegroupAsync(
        Guid jobId, string roleId, Guid targetAgegroupId, Guid teamId,
        CancellationToken ct = default);

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
    /// <paramref name="asOfDate"/> null = ignore the date window (configured amounts regardless of
    /// start/end) — used to resolve the window-independent late fee that caps the paid lock.
    /// </summary>
    Task<ResolvedModifiers> EvaluateModifiersAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        DateTime? asOfDate,
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
    /// Would moving <paramref name="reg"/> onto the target team strand its Authorize.Net recurring
    /// billing plan? Returns null when the move is safe, otherwise the figures a caller needs to
    /// explain the refusal. READ-ONLY — mutates nothing, persists nothing.
    /// <para>
    /// An ARB plan drafts one FIXED amount per occurrence, decided when the plan was minted. A swap
    /// re-prices the registration (<see cref="ApplySwapFeesAsync"/>) but there is no
    /// <c>ARBUpdateSubscription</c> anywhere in this stack — the plan cannot follow. The two then
    /// disagree permanently: the drafts under-collect against a dearer team, or over-collect against
    /// a cheaper one with nothing to detect it.
    /// </para>
    /// <para>
    /// Worse, the arrears figure the family is shown assumes the plan still finances the fee —
    /// <c>AdnSweepService.ComputeInstallmentMath</c> computes
    /// <c>FeeTotal - PaidTotal - (amountPerOccurrence * remaining)</c>. Break that assumption and the
    /// error is exactly <c>amountPerOccurrence * remaining</c>, in the family's email AND on the
    /// CC-update page it links to.
    /// </para>
    /// <para>
    /// Two conditions, both required:
    /// </para>
    /// <list type="number">
    /// <item>The plan has occurrences STILL TO DRAFT. Keyed on schedule position, never on
    /// <c>AdnSubscriptionStatus</c> — that column is a local mirror that can read "active" long after
    /// the last draft. A finished plan is provably safe: at <c>remaining = 0</c> the formula above
    /// collapses to <c>FeeTotal - PaidTotal</c>, an ordinary balance or credit.</item>
    /// <item>The move actually changes the money — compared on the resulting <c>FeeTotal</c>, NOT on
    /// the target team's sticker price. Payment phase is a per-scope override (team → agegroup →
    /// league) and processing re-derives off the new base, so two teams at the SAME advertised price
    /// can still land the registration on different totals.</item>
    /// </list>
    /// </summary>
    Task<ArbPlanConflict?> DetectArbPlanConflictAsync(
        Registrations reg, Guid jobId, Guid targetAgegroupId, Guid targetTeamId,
        CancellationToken ct = default);

    /// <summary>
    /// Pre-hydrated variant of <see cref="ApplySwapFeesAsync"/> for the whole-job reprice
    /// engines: the caller batch-resolves the fee cascade (<see cref="ResolveFeesByTeamIdsAsync"/>)
    /// and PaymentStates (<c>IPaymentStateService.ForRegistrationsAsync</c>) up front — a handful
    /// of queries for the whole job instead of ~6 per registration — and this stamps one
    /// registration with ZERO DB round-trips. Identical math: both paths run the same private
    /// core (phase decision incl. paid-past-deposit promotion, FeeBase, FeeProcessing, totals).
    /// <c>ctx.AssessActiveLateFee</c> must be false (throws): late-fee re-derivation needs
    /// per-entity modifier reads, and reprices never assess it — late fees mint at charge entry.
    /// </summary>
    void ApplySwapFees(
        Registrations reg, ResolvedFee? resolved, Payments.PaymentState state, FeeApplicationContext ctx);

    /// <summary>
    /// Charge-entry realize for a PLAYER registration: re-derive the effective late fee (and
    /// recompute processing + totals) for a single reg about to be charged, so OwedTotal reflects
    /// an auto-activated late-fee window WITHOUT a prior director reprice. DRY — delegates to
    /// <see cref="ApplySwapFeesAsync"/> with <c>AssessActiveLateFee = true</c> (the exact path the
    /// reprice engine uses), keying the cascade off the supplied agegroup/team. Does NOT persist;
    /// the charge caller saves. Inert in the common case (no active window or fully paid ⇒ no change
    /// ⇒ no AMOUNT_MISMATCH); only a genuinely owed reg inside an open window moves.
    /// </summary>
    Task RealizeLateFeeAtChargeAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default);

    /// <summary>
    /// Charge-entry realize for a TEAM (club-rep) entity — the team-path twin of
    /// <see cref="RealizeLateFeeAtChargeAsync(Registrations, Guid, Guid, Guid, CancellationToken)"/>.
    /// Re-derives the effective late fee via <see cref="ApplyTeamSwapFeesAsync"/> with
    /// <c>AssessActiveLateFee = true</c>, keying the cascade off the team's own agegroup. Does NOT
    /// persist; the charge caller saves.
    /// </summary>
    Task RealizeLateFeeAtChargeAsync(
        Domain.Entities.Teams team, Guid jobId,
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

    // ── Team-scoped fee rows: scope invariant ───────────────────

    /// <summary>
    /// Repoints a team's team-scoped <c>fees.JobFees</c> rows onto the agegroup the team now
    /// lives in. THE invariant: a team-scoped row's AgegroupId always equals its team's, because
    /// the team tier is really keyed by (JobId, RoleId, TeamId) — a team is in exactly one
    /// agegroup. Without this, moving a team leaves its pricing pinned to the old agegroup, where
    /// the cascade's team tier (which matches the (AgegroupId, TeamId) PAIR) can no longer see it,
    /// and the team silently falls back to the target agegroup's price.
    ///
    /// Call this on EVERY write of <c>Teams.AgegroupId</c>, and call it BEFORE any fee
    /// (re)resolution for the move: resolution reads the database, so an unflushed repoint is
    /// invisible to it and the stamp would use the agegroup price.
    ///
    /// Where a team already has more than one team-scoped row for a role (possible only from
    /// moves made before this invariant existed — <c>UX_JobFees_Scope</c> is unique on
    /// (JobId, RoleId, AgegroupId, TeamId), which does NOT constrain the team tier's real key),
    /// the newest <c>Modified</c> wins as the director's most recent expressed intent and the
    /// rest are retired, so the cascade can never pick between two prices.
    ///
    /// Does NOT persist — the caller saves, as everywhere else in this service.
    /// </summary>
    Task RepointTeamScopedFeesAsync(
        Guid teamId, Guid targetAgegroupId, string? userId, CancellationToken ct = default);

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

    /// <summary>
    /// Pre-hydrated variant of <see cref="ApplyTeamSwapFeesAsync"/> — the team twin of
    /// <see cref="ApplySwapFees"/>. The whole-job team reprice batch-resolves the ClubRep
    /// cascade and PaymentStates up front and stamps each team with ZERO DB round-trips,
    /// through the same private core as the async path. <c>ctx.AssessActiveLateFee</c> must
    /// be false (throws) — see <see cref="ApplySwapFees"/>.
    /// </summary>
    void ApplyTeamSwapFees(
        Domain.Entities.Teams team, ResolvedFee? resolved, Payments.PaymentState state,
        TeamFeeApplicationContext ctx);
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
/// Context for player fee application — controls processing fee behavior and reprice
/// edge cases. Payment phase is NOT in this context: FeeResolutionService resolves it
/// per scope via ResolvedFee.ResolveFullPaymentPhase (a team/agegroup/league JobFees
/// override wins; no override = deposit phase — the legacy job-level baseline columns
/// are abandoned). Effective full-payment → FeeBase = Deposit + BalanceDue; effective
/// deposit phase → FeeBase = Deposit (or BalanceDue when no deposit configured).
/// ApplyPifUpgradeAsync remains the per-registration "parent voluntarily pays in full
/// at checkout" path.
///
/// NonCcPayments is NOT in this context — FeeResolutionService looks it up from the
/// registration's payment history when stamping FeeProcessing.
/// </summary>
public record FeeApplicationContext
{
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

    /// <summary>
    /// When true, a registration ALREADY stamped at the full price is treated as full-payment
    /// phase and is NOT re-derived down to the deposit — even when config + payment history would
    /// say "deposit". Set ONLY by the at-charge late-fee realize (RealizeLateFeeAtChargeAsync): a
    /// parent who chose Pay-in-Full on a deposit-phase job has just had the reg upgraded to full
    /// (FeeBase = FullPrice) with nothing paid yet, so the payment-history promotion cannot fire;
    /// without this the realize would revert the upgrade to the deposit and the AMOUNT_MISMATCH
    /// tripwire would refuse the charge. Default false: roster-swap/club-roster/seat/waitlist/
    /// director-recalc callers keep the config-driven phase so a genuine move can still re-phase a
    /// reg. Narrow by design — it only ever PRESERVES an existing full stamp, never forces full.
    /// </summary>
    public bool PreserveFullPaymentStamp { get; init; }
}

/// <summary>
/// Why a move would strand a registrant's Authorize.Net recurring-billing plan — the output of
/// <see cref="IFeeResolutionService.DetectArbPlanConflictAsync"/>. Every field is a figure the
/// director needs to act: where the plan stands, what it drafts, and what the move does to the bill.
/// A non-null instance IS the refusal; there is no severity to weigh.
/// </summary>
public sealed record ArbPlanConflict
{
    /// <summary>Occurrences already drafted (scheduled on or before today).</summary>
    public required int OccurrencesToDate { get; init; }

    /// <summary>Occurrences in the whole plan.</summary>
    public required int TotalOccurrences { get; init; }

    /// <summary>Occurrences still to draft. Always &gt; 0 — a finished plan is not a conflict.</summary>
    public required int OccurrencesRemaining { get; init; }

    /// <summary>The fixed per-draft amount. This is what the plan keeps taking, right or wrong.</summary>
    public required decimal AmountPerOccurrence { get; init; }

    /// <summary>The registration's FeeTotal as it stands today.</summary>
    public required decimal CurrentFeeTotal { get; init; }

    /// <summary>The FeeTotal the move would stamp — from a dry run of the real applier, not an estimate.</summary>
    public required decimal NewFeeTotal { get; init; }
}

/// <summary>
/// Context for team fee application — controls phase and processing fee behavior.
///
/// NonCcPayments is NOT in this context — FeeResolutionService looks it up from the
/// team's payment history when stamping FeeProcessing.
/// </summary>
public record TeamFeeApplicationContext
{
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
