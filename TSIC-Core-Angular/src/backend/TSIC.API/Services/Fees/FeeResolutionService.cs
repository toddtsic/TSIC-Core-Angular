using TSIC.Contracts.Extensions;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

using TeamsEntity = TSIC.Domain.Entities.Teams;

namespace TSIC.API.Services.Fees;

/// <summary>
/// Single source of truth for fee resolution and application.
/// Reads from fees.JobFees via cascade, stamps results onto Registration/Team snapshot fields.
/// All payment-state interpretation (CC reverse-out, eCheck principal/proc, recalc target)
/// is delegated to <see cref="IPaymentStateService"/> so this service never touches raw
/// PaymentMethod strings or rate math.
/// </summary>
public sealed class FeeResolutionService : IFeeResolutionService
{
    private readonly IFeeRepository _feeRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IPaymentStateService _paymentState;

    public FeeResolutionService(
        IFeeRepository feeRepo,
        IJobRepository jobRepo,
        IPaymentStateService paymentState)
    {
        _feeRepo = feeRepo;
        _jobRepo = jobRepo;
        _paymentState = paymentState;
    }

    // ── Processing Fee Rate ─────────────────────────────────────

    public async Task<decimal> GetEffectiveProcessingRateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobPercent = await _jobRepo.GetProcessingFeePercentAsync(jobId, ct);
        // Single rate canonical: clamp + percent→multiplier lives only in ProcessingRateMath.
        return ProcessingRateMath.ToCcMultiplier(jobPercent);
    }

    public async Task<decimal> GetEffectiveEcheckProcessingRateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobPercent = await _jobRepo.GetEcprocessingFeePercentAsync(jobId, ct);
        return ProcessingRateMath.ToEcheckMultiplier(jobPercent);
    }

    private async Task<bool> GetAddProcessingFeesAsync(Guid jobId, CancellationToken ct)
    {
        var settings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);
        return settings?.BAddProcessingFees ?? false;
    }

    // ── Resolution ──────────────────────────────────────────────

    public async Task<ResolvedFee?> ResolveFeeAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default)
    {
        return await _feeRepo.GetResolvedFeeAsync(jobId, roleId, agegroupId, teamId, ct);
    }

    public async Task<ResolvedFee?> ResolveFeeForAgegroupAsync(
        Guid jobId, string roleId, Guid agegroupId,
        CancellationToken ct = default)
    {
        return await _feeRepo.GetResolvedFeeForAgegroupAsync(jobId, roleId, agegroupId, ct);
    }

    public async Task<ResolvedFee?> ResolveFeeForTeamAtAgegroupAsync(
        Guid jobId, string roleId, Guid targetAgegroupId, Guid teamId,
        CancellationToken ct = default)
    {
        return await _feeRepo.GetResolvedFeeForTeamAtAgegroupAsync(
            jobId, roleId, targetAgegroupId, teamId, ct);
    }

    public async Task<Dictionary<Guid, ResolvedFee>> ResolveFeesByTeamIdsAsync(
        Guid jobId, string roleId, IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default)
    {
        return await _feeRepo.GetResolvedFeesByTeamIdsAsync(jobId, roleId, teamIds, ct);
    }

    public async Task<ResolvedModifiers> EvaluateModifiersAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        DateTime? asOfDate,
        CancellationToken ct = default)
    {
        var modifiers = await _feeRepo.GetActiveModifiersForCascadeAsync(
            jobId, roleId, agegroupId, teamId, asOfDate, ct);

        return new ResolvedModifiers
        {
            TotalDiscount = modifiers
                .Where(m => m.ModifierType == FeeConstants.ModifierEarlyBird)
                .Sum(m => m.Amount),
            TotalLateFee = modifiers
                .Where(m => m.ModifierType == FeeConstants.ModifierLateFee)
                .Sum(m => m.Amount)
        };
    }

    /// <summary>
    /// The late fee that currently applies to an entity under the derived "pay late ⇒ owe more"
    /// model. Resolves the active (windowed, as-of-now) late fee for the GATE and the configured
    /// (window-independent) late fee for the paid-lock FLOOR, then defers to
    /// <see cref="PaymentState.EffectiveLateFee"/>. Every recompute/payment path calls this so the
    /// late fee is re-derived live and locked only once its dollars are collected.
    /// </summary>
    private async Task<decimal> ResolveEffectiveLateFeeAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        PaymentState state, decimal fullPrice, decimal depositBase, decimal discount, decimal donation,
        CancellationToken ct)
    {
        // Sequential awaits (shared scoped DbContext) — never Task.WhenAll.
        var windowed = (await EvaluateModifiersAsync(jobId, roleId, agegroupId, teamId, DateTime.Now, ct)).TotalLateFee;
        var configured = (await EvaluateModifiersAsync(jobId, roleId, agegroupId, teamId, null, ct)).TotalLateFee;
        // depositBase = the deposit tier the late fee was billed on (resolved EffectiveDeposit) — the
        // paid-lock FLOOR allocates against it so a late fee collected with the deposit survives a later
        // deposit→full-payment flip. fullPrice still drives the in-window GATE.
        return state.EffectiveLateFee(windowed, configured, fullPrice, depositBase, discount, donation);
    }

    // ── Resolution (Job-level) ────────────────────────────────

    public async Task<ResolvedFee?> ResolveJobLevelFeeAsync(
        Guid jobId, string roleId,
        CancellationToken ct = default)
    {
        return await _feeRepo.GetJobLevelFeeAsync(jobId, roleId, ct);
    }

    // ── Adult Registration: Staff with team assignment ─────────

    public async Task ApplyNewStaffRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Staff, agegroupId, teamId, ct);
        // Adult/coach roles were free in legacy (StaffTournamentController charged
        // nothing) and have no fee-config UI or seed. An unconfigured resolution here
        // means "free" ($0), NOT a misconfiguration — so unlike the paid Player/ClubRep
        // paths we default to $0 rather than fail loud. A Staff JobFees row, if ever
        // added manually, still prices normally.
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;

        var modifiers = await EvaluateModifiersAsync(
            jobId, RoleConstants.Staff, agegroupId, teamId, DateTime.Now, ct);

        reg.FeeBase = baseFee;
        reg.FeeDiscount = modifiers.TotalDiscount;
        reg.FeeLatefee = modifiers.TotalLateFee;

        await ApplyRegistrationProcessingAndTotalsAsync(reg, jobId, isNew: true, ct);
    }

    // ── Adult Registration: New (UA, Referee, Recruiter — no team) ──

    public async Task ApplyNewAdultRegistrationFeesAsync(
        Registrations reg, Guid jobId, string roleId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveJobLevelFeeAsync(jobId, roleId, ct);
        // See ApplyNewStaffRegistrationFeesAsync: adult roles (UA/Referee/Recruiter)
        // were free in legacy and have no fee-config UI or seed, so an unconfigured
        // resolution defaults to $0 rather than failing loud.
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;

        // Evaluate job-level modifiers only (no agegroup/team)
        var modifiers = await _feeRepo.GetActiveModifiersForJobLevelAsync(
            jobId, roleId, DateTime.Now, ct);

        var totalDiscount = modifiers
            .Where(m => m.ModifierType == FeeConstants.ModifierEarlyBird)
            .Sum(m => m.Amount);
        var totalLateFee = modifiers
            .Where(m => m.ModifierType == FeeConstants.ModifierLateFee)
            .Sum(m => m.Amount);

        reg.FeeBase = baseFee;
        reg.FeeDiscount = totalDiscount;
        reg.FeeLatefee = totalLateFee;

        await ApplyRegistrationProcessingAndTotalsAsync(reg, jobId, isNew: true, ct);
    }

    // ── Player Registration: New ────────────────────────────────

    /// <summary>
    /// Initial fee stamp at team-reservation time. Phase = the canonical
    /// <see cref="ResolvedFee.ResolveFullPaymentPhase"/>: a per-scope JobFees override
    /// (team → agegroup → league) wins; no override = deposit phase (the legacy
    /// job-level baseline is abandoned). Deposit phase → FeeBase = Deposit (or
    /// BalanceDue when no deposit configured); full-payment phase → FeeBase =
    /// Deposit + BalanceDue.
    /// </summary>
    public async Task ApplyNewRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, agegroupId, teamId, ct);
        if (resolved is not { FeeConfigured: true })
            throw new FeeNotConfiguredException(jobId, RoleConstants.Player, agegroupId, teamId);
        var deposit = resolved.EffectiveDeposit;
        var balanceDue = resolved.EffectiveBalanceDue;

        decimal baseFee;
        if (ResolvedFee.ResolveFullPaymentPhase(resolved))
        {
            baseFee = resolved.FullPrice;
        }
        else
        {
            baseFee = deposit > 0m ? deposit : balanceDue;
        }

        var modifiers = await EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agegroupId, teamId, DateTime.Now, ct);

        reg.FeeBase = baseFee;
        reg.FeeDiscount = modifiers.TotalDiscount;
        reg.FeeLatefee = modifiers.TotalLateFee;

        await ApplyRegistrationProcessingAndTotalsAsync(reg, jobId, isNew: true, ct);
    }

    // ── Player Registration: PIF Upgrade (checkout) ─────────────

    public async Task ApplyPifUpgradeAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, agegroupId, teamId, ct);

        // PIF = pay the whole fee now. FullPrice is THE shared "deposit + balance" formula
        // (NULL deposit → balance only, never double-counted), identical to the reserve-time
        // stamp above — so the checkout recompute can never diverge from the screen total.
        reg.FeeBase = resolved?.FullPrice ?? 0m;
        // FeeDiscount / FeeLatefee / FeeDonation preserved from initial stamp. The late fee is NOT
        // re-derived at the charge here: the player/team payment paths run an AMOUNT_MISMATCH
        // tripwire (PaymentService) that rejects a charge differing from what the client was shown,
        // so introducing a late fee the display didn't preview would fail the payment. The late fee
        // reaches owing registrants via the director's fee-save reprice (which re-derives + stamps);
        // live preview-at-charge is the Phase 2 read-path change that keeps display and charge in sync.

        await ApplyRegistrationProcessingAndTotalsAsync(reg, jobId, isNew: false, ct);
    }

    // ── Player Registration: Recompute (modifier already on the row) ──

    public Task RecomputeRegistrationFinancialsAsync(
        Registrations reg, Guid jobId, CancellationToken ct = default)
        => ApplyRegistrationProcessingAndTotalsAsync(reg, jobId, isNew: false, ct);

    // ── Player Registration: Swap ───────────────────────────────

    public async Task ApplySwapFeesAsync(
        Registrations reg, Guid jobId, Guid targetAgegroupId, Guid targetTeamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, targetAgegroupId, targetTeamId, ct);
        // The same PaymentState drives the phase decision (paid-past-deposit promotion) and the
        // totals recompute below — one ledger read, not two.
        var state = await _paymentState.ForRegistrationAsync(reg.RegistrationId, jobId, ct);

        StampSwapFeeBase(reg, resolved, state, ctx);

        // Late fee: re-derive live (derived "pay late ⇒ owe more" model). A recompute that means to
        // (re)assess the late fee opts in via ctx.AssessActiveLateFee — the director's "update all
        // prior" reprice and the payment recompute. EffectiveLateFee both ADDS an in-window fee to a
        // reg that owes AND holds the floor for one already paid (so it survives the window closing),
        // and drops/reduces it when the modifier is deleted/edited (overpay → negative owed → refund).
        // Pure roster swaps (flag false) leave the existing fee frozen — a move never conjures a penalty.
        if (ctx.AssessActiveLateFee)
        {
            reg.FeeLatefee = await ResolveEffectiveLateFeeAsync(
                jobId, RoleConstants.Player, targetAgegroupId, targetTeamId,
                state, resolved?.FullPrice ?? 0m, resolved?.EffectiveDeposit ?? 0m,
                reg.TotalDiscount(), reg.FeeDonation, ct);
        }

        StampRegistrationProcessingAndTotals(reg, state);
    }

    public void ApplySwapFees(
        Registrations reg, ResolvedFee? resolved, PaymentState state, FeeApplicationContext ctx)
    {
        // Late-fee assessment needs per-entity modifier reads — only the async path can honor it.
        // The reprice engines (the only pre-hydrated callers) never assess it: a late fee mints
        // at charge entry (RealizeLateFeeAtChargeAsync), not on a reprice.
        if (ctx.AssessActiveLateFee)
            throw new ArgumentException(
                "AssessActiveLateFee requires ApplySwapFeesAsync (per-entity modifier reads).", nameof(ctx));

        StampSwapFeeBase(reg, resolved, state, ctx);
        StampRegistrationProcessingAndTotals(reg, state);
    }

    /// <summary>
    /// Phase decision + FeeBase stamp for a player swap/reprice. Phase is decided from BOTH the
    /// config cascade AND the registrant's own payments:
    ///   (1) Config: per-scope override (team → ag → league) ?? job baseline.
    ///   (2) Promotion: having paid PAST the deposit tier IS entering full payment. The
    ///       registrant's payment history overrides the scope's deposit-phase default, so a
    ///       fee/phase change can never re-stamp a paid-ahead reg DOWN to the deposit (which
    ///       would net a bogus credit), AND a price increase correctly reaches already-paid
    ///       registrants — they owe the delta.
    /// The threshold is principal-based (proc backed out per method via PaymentState) compared
    /// against the discount/late/donation-adjusted deposit, with a small tolerance so a reg
    /// that paid EXACTLY its deposit is not spuriously promoted.
    /// </summary>
    private static void StampSwapFeeBase(
        Registrations reg, ResolvedFee? resolved, PaymentState state, FeeApplicationContext ctx)
    {
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        const decimal depositPaidTolerance = 0.01m;
        var effectiveDeposit = Math.Max(0m, deposit - reg.TotalDiscount() + reg.FeeLatefee + reg.FeeDonation);
        // Principal from the STORED totals, never the ledger decode (StoredTotalsMath) — a
        // legacy flat-principal CC row decodes ~3.4% short and could hold a paid-past reg
        // in the deposit tier it has actually left.
        var (_, principalPaid) = StoredTotalsMath.Split(
            reg.PaidTotal, reg.OwedTotal, reg.FeeProcessing, state.CcRate, state.BAddProcessingFees);
        var paidPastDeposit = principalPaid > effectiveDeposit + depositPaidTolerance;

        // A reg already stamped at the full price is in full-payment phase and must NOT be re-derived
        // down to the deposit — the same "stamped FeeBase is authoritative" signal PaymentService
        // .IsRegFullPaymentPhase uses. Gated behind PreserveFullPaymentStamp so only the at-charge
        // realize opts in (a fresh Pay-in-Full upgrade has FeeBase = FullPrice but nothing paid, so
        // the paidPastDeposit promotion can't rescue it); genuine swaps/recalcs keep re-phasing.
        var fullPrice = resolved?.FullPrice ?? 0m;
        var alreadyFullStamped = ctx.PreserveFullPaymentStamp && fullPrice > 0m && reg.FeeBase >= fullPrice - 0.005m;
        var fullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved) || paidPastDeposit || alreadyFullStamped;
        reg.FeeBase = fullPayment
            ? fullPrice
            : (deposit > 0m ? deposit : balanceDue);
        // FeeDiscount / FeeLatefee / FeeDonation preserved
    }

    // ── Team Entity: New ────────────────────────────────────────

    public async Task ApplyNewTeamFeesAsync(
        TeamsEntity team, Guid jobId, Guid agegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.ClubRep, agegroupId, team.TeamId, ct);
        if (resolved is not { FeeConfigured: true })
            throw new FeeNotConfiguredException(jobId, RoleConstants.ClubRep, agegroupId, team.TeamId);

        var deposit = resolved.EffectiveDeposit;
        var balanceDue = resolved.EffectiveBalanceDue;

        // Per-scope override (team → ag → league); no override = deposit. Decided ONCE
        // and threaded into the proc/totals helper so phase can never disagree there.
        var fullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved);
        var feeBase = fullPayment ? resolved.FullPrice : deposit;

        var modifiers = await EvaluateModifiersAsync(
            jobId, RoleConstants.ClubRep, agegroupId, team.TeamId, DateTime.Now, ct);

        team.FeeBase = feeBase;
        team.FeeDiscount = modifiers.TotalDiscount;
        team.FeeLatefee = modifiers.TotalLateFee;

        await ApplyTeamProcessingAndTotalsAsync(
            team, jobId, deposit, balanceDue, ctx, fullPayment, isNew: true,
            rawDeposit: resolved.Deposit ?? 0m, ct);
    }

    // ── Team-scoped fee rows: scope invariant ───────────────────

    public async Task RepointTeamScopedFeesAsync(
        Guid teamId, Guid targetAgegroupId, string? userId, CancellationToken ct = default)
    {
        var rows = await _feeRepo.GetTrackedByTeamIdAsync(teamId, ct);
        if (rows.Count == 0) return;

        var now = DateTime.Now;

        // One team can carry a row per role (Player and ClubRep price independently), so the
        // winner/retire decision is made PER (job, role) — never across roles.
        foreach (var group in rows.GroupBy(r => (r.JobId, r.RoleId)))
        {
            var ordered = group.OrderByDescending(r => r.Modified).ToList();
            var winner = ordered[0];
            var atTarget = ordered.FirstOrDefault(r => r.AgegroupId == targetAgegroupId);

            if (atTarget != null && !ReferenceEquals(atTarget, winner))
            {
                // A row already occupies the (JobId, RoleId, targetAgegroupId, TeamId) slot that
                // UX_JobFees_Scope makes unique. Merge the winner's VALUES down into it instead of
                // repointing the winner onto an occupied slot — that would be a unique violation,
                // and inside a pool transfer it fails the whole move, not just the fee.
                atTarget.Deposit = winner.Deposit;
                atTarget.BalanceDue = winner.BalanceDue;
                atTarget.BFullPaymentRequired = winner.BFullPaymentRequired;
                atTarget.Modified = now;
                atTarget.LebUserId = userId;

                // Modifiers hang off JobFeeId. Re-mint the winner's onto the surviving row rather
                // than re-pointing the existing rows, so the winner's own copies are free to be
                // deleted with it below (re-pointing then deleting would take them with it).
                foreach (var stale in atTarget.FeeModifiers.ToList())
                    _feeRepo.RemoveModifier(stale);
                foreach (var m in winner.FeeModifiers)
                    _feeRepo.AddModifier(new FeeModifiers
                    {
                        FeeModifierId = Guid.NewGuid(),
                        JobFeeId = atTarget.JobFeeId,
                        ModifierType = m.ModifierType,
                        Amount = m.Amount,
                        StartDate = m.StartDate,
                        EndDate = m.EndDate,
                        Modified = now,
                        LebUserId = userId
                    });

                winner = atTarget;
            }
            else if (winner.AgegroupId != targetAgegroupId)
            {
                // Target slot is free — repoint in place. Keeping the row id means the row's
                // modifiers (early bird / late fee) ride along untouched, which is the whole
                // reason this is an UPDATE and not a delete-and-recreate.
                winner.AgegroupId = targetAgegroupId;
                winner.Modified = now;
                winner.LebUserId = userId;
            }

            // Retire everything else for this (job, role, team). By construction none of these
            // sit at the target slot, so a delete can never race the update above inside one
            // SaveChanges regardless of the order EF picks.
            foreach (var loser in ordered.Where(r => !ReferenceEquals(r, winner)))
            {
                foreach (var m in loser.FeeModifiers.ToList())
                    _feeRepo.RemoveModifier(m);
                _feeRepo.Remove(loser);
            }
        }
    }

    // ── Team Entity: Swap ───────────────────────────────────────

    public async Task ApplyTeamSwapFeesAsync(
        TeamsEntity team, Guid jobId, Guid targetAgegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.ClubRep, targetAgegroupId, team.TeamId, ct);
        // The same PaymentState drives the phase decision (paid-past-deposit promotion) and the
        // totals recompute below — one ledger read, not two. Hydration is slice-aware
        // internally (proc-on-balance-only jobs decompose CC/eCheck gross without inventing
        // proc on the deposit slice).
        var state = await _paymentState.ForTeamAsync(team.TeamId, jobId, ct);

        var (deposit, balanceDue, fullPayment) = StampTeamSwapFeeBase(team, resolved, state, ctx);

        // Late fee: re-derive live (derived "pay late ⇒ owe more" model) when this recompute opts in
        // via ctx.AssessActiveLateFee (the director's "update all prior" reprice, and the team
        // payment recompute). EffectiveLateFee adds an in-window fee to a team that owes, holds the
        // floor for one already paid (survives the window closing), and drops/reduces it on
        // delete/edit (overpay → negative owed → refund). Pure roster moves (flag false) freeze it.
        if (ctx.AssessActiveLateFee)
        {
            team.FeeLatefee = await ResolveEffectiveLateFeeAsync(
                jobId, RoleConstants.ClubRep, targetAgegroupId, team.TeamId,
                state, resolved?.FullPrice ?? 0m, resolved?.EffectiveDeposit ?? 0m,
                team.TotalDiscount(), team.FeeDonation ?? 0m, ct);
        }

        StampTeamProcessingAndTotals(team, deposit, balanceDue, ctx, fullPayment, state,
            rawDeposit: resolved?.Deposit ?? 0m);
    }

    public void ApplyTeamSwapFees(
        TeamsEntity team, ResolvedFee? resolved, PaymentState state, TeamFeeApplicationContext ctx)
    {
        // Late-fee assessment needs per-entity modifier reads — only the async path can honor it.
        // The reprice engines (the only pre-hydrated callers) never assess it: a late fee mints
        // at charge entry (RealizeLateFeeAtChargeAsync), not on a reprice.
        if (ctx.AssessActiveLateFee)
            throw new ArgumentException(
                "AssessActiveLateFee requires ApplyTeamSwapFeesAsync (per-entity modifier reads).", nameof(ctx));

        var (deposit, balanceDue, fullPayment) = StampTeamSwapFeeBase(team, resolved, state, ctx);
        StampTeamProcessingAndTotals(team, deposit, balanceDue, ctx, fullPayment, state,
            rawDeposit: resolved?.Deposit ?? 0m);
    }

    /// <summary>
    /// Phase decision + FeeBase stamp for a team swap/reprice. Phase is decided from BOTH the
    /// config cascade AND the team's own payments — identical to the player swap applier:
    ///   (1) Config: per-scope override (team → ag → league) ?? job baseline (ctx).
    ///   (2) Promotion: having paid PAST the deposit tier IS entering full payment. This makes
    ///       the reprice engine's old OwedTotal&lt;=0 skip unnecessary — a paid-ahead (or
    ///       owed-zeroed) team is re-stamped to FullPrice, NEVER down to the deposit, so a
    ///       PIF→deposit downgrade can't net a bogus credit, AND a deposit→PIF upgrade reaches
    ///       a team whose deposit-phase owed was already zeroed (e.g. by a correction).
    /// Threshold is principal-based (proc backed out per method via PaymentState), compared
    /// against the discount/late/donation-adjusted deposit with a small tolerance so a team
    /// that paid EXACTLY its deposit is not spuriously promoted. FeeBase changes for the phase;
    /// discount/donation stay frozen (the late fee is the async caller's opt-in concern).
    /// </summary>
    private static (decimal Deposit, decimal BalanceDue, bool FullPayment) StampTeamSwapFeeBase(
        TeamsEntity team, ResolvedFee? resolved, PaymentState state, TeamFeeApplicationContext ctx)
    {
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        const decimal depositPaidTolerance = 0.01m;
        var effectiveDeposit = Math.Max(
            0m, deposit - team.TotalDiscount() + (team.FeeLatefee ?? 0m) + (team.FeeDonation ?? 0m));
        // Principal from the STORED totals, never the ledger decode (StoredTotalsMath) — a
        // legacy flat-principal CC row decodes ~3.4% short and could hold a paid-past team
        // in the deposit tier it has actually left.
        var (_, principalPaid) = StoredTotalsMath.Split(
            team.PaidTotal ?? 0m, team.OwedTotal ?? 0m, team.FeeProcessing ?? 0m,
            state.CcRate, state.BAddProcessingFees);
        var paidPastDeposit = principalPaid > effectiveDeposit + depositPaidTolerance;

        var fullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved) || paidPastDeposit;
        team.FeeBase = fullPayment ? (resolved?.FullPrice ?? 0m) : deposit;

        return (deposit, balanceDue, fullPayment);
    }

    // ── Charge-entry realize (auto-activated late fee) ──────────
    //
    // These are the read/charge-side twin of the reprice engines: they run the SAME swap applier
    // with AssessActiveLateFee=true so an auto-activated late-fee window lands at payment without a
    // director reprice. They build the per-role context from the canonical job-settings accessors
    // (identical values to RecalculatePlayer/TeamFeesAsync) and do NOT persist — the charge caller
    // owns SaveChanges. Inert when no window is active or the record is fully paid.

    public async Task RealizeLateFeeAtChargeAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default)
    {
        await ApplySwapFeesAsync(
            reg, jobId, agegroupId, teamId,
            new FeeApplicationContext
            {
                AssessActiveLateFee = true,
                // A parent who chose Pay-in-Full on a deposit-phase job has already had this reg
                // upgraded to FeeBase = FullPrice (nothing paid yet). Preserve that stamp — do not
                // let the phase re-derivation demote it back to the deposit and trip AMOUNT_MISMATCH.
                PreserveFullPaymentStamp = true
            },
            ct);
    }

    public async Task RealizeLateFeeAtChargeAsync(
        TeamsEntity team, Guid jobId,
        CancellationToken ct = default)
    {
        var settings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);
        var processingRate = await GetEffectiveProcessingRateAsync(jobId, ct);
        await ApplyTeamSwapFeesAsync(
            team, jobId, team.AgegroupId,
            new TeamFeeApplicationContext
            {
                AddProcessingFees = settings?.BAddProcessingFees ?? false,
                ApplyProcessingFeesToDeposit = settings?.BApplyProcessingFeesToTeamDeposit ?? false,
                ProcessingFeePercent = processingRate,
                AssessActiveLateFee = true
            },
            ct);
    }

    // ── Private: Processing + Totals (canonical) ────────────────
    //
    // FeeProcessing is resolved to the invariant
    //   FeeProcessing = ProcCollected + remainingCcBillable × CcRate
    // — proc already taken at swipe (CC + eCheck) plus proc that would be
    // collected if the rest were CC-billed. Per-payment handlers maintain the
    // same invariant incrementally.
    //
    // ProcCollected / PrincipalPaid come from the STORED entity totals via
    // StoredTotalsMath.Split — NEVER from PaymentState's ledger decode
    // (payamt ÷ (1+rate)). The decode is only valid for rows the current
    // charge engine wrote; legacy club-lump allocation rows book flat
    // principal, and decoding them minted ~3.4% phantom proc → real
    // owed_total on every reprice of a settled legacy team (proven in
    // LegacyFlatCcLedgerRepriceTests: $45.50 on a $1,300 balance). For
    // engine-written entities the split reproduces the decode to the cent,
    // so this is a strict no-op for self-consistent data. PaymentState still
    // supplies the phase-promotion inputs and the rate/config context.

    private async Task ApplyRegistrationProcessingAndTotalsAsync(
        Registrations reg, Guid jobId, bool isNew, CancellationToken ct, PaymentState? state = null)
    {
        // Callers that already resolved the registrant's PaymentState (the swap/reprice path,
        // which needs it for the paid-past-deposit promotion) pass it through to avoid a second
        // ledger read. Otherwise resolve it here: Empty for a brand-new reg, else from the ledger.
        state ??= isNew
            ? PaymentState.Empty(
                await GetAddProcessingFeesAsync(jobId, ct),
                await GetEffectiveProcessingRateAsync(jobId, ct),
                await GetEffectiveEcheckProcessingRateAsync(jobId, ct))
            : await _paymentState.ForRegistrationAsync(reg.RegistrationId, jobId, ct);

        StampRegistrationProcessingAndTotals(reg, state);
    }

    private static void StampRegistrationProcessingAndTotals(Registrations reg, PaymentState state)
    {
        // TotalDiscount() — the proc basis must net the same discount FeeMath subtracts from FeeTotal,
        // or the proc target disagrees with the total it is a component of.
        // Read the OLD totals before writing: FeeBase was re-stamped by the caller, but
        // FeeProcessing/PaidTotal/OwedTotal still hold the pre-reprice books the split needs.
        reg.FeeProcessing = reg.FeeBase > 0m && state.BAddProcessingFees
            ? RegistrationFeeProcessingTarget(reg, state.CcRate)
            : 0m;

        reg.RecalcTotals();
    }

    /// <summary>
    /// Entity-anchored FeeProcessing for a player reg (no proc-free slice — player proc
    /// rides the whole bill): proc collected so far + rate on the principal still unpaid
    /// against the NEW base. Same structure as PaymentState.FeeProcessingTarget with the
    /// paid split taken from the stored totals instead of the ledger decode.
    /// </summary>
    private static decimal RegistrationFeeProcessingTarget(Registrations reg, decimal ccRate)
    {
        var (procCollected, principalPaid) = StoredTotalsMath.Split(
            reg.PaidTotal, reg.OwedTotal, reg.FeeProcessing, ccRate, bAddProcessingFees: true);
        var principalBase = reg.FeeBase - reg.TotalDiscount() + reg.FeeLatefee + reg.FeeDonation;
        var remaining = Math.Max(0m, principalBase - principalPaid);
        return Math.Round(procCollected + remaining * ccRate, 2, MidpointRounding.AwayFromZero);
    }

    private async Task ApplyTeamProcessingAndTotalsAsync(
        TeamsEntity team, Guid jobId, decimal deposit, decimal balanceDue,
        TeamFeeApplicationContext ctx, bool fullPayment, bool isNew, decimal rawDeposit,
        CancellationToken ct, PaymentState? state = null)
    {
        if (state is null)
        {
            // Callers that already resolved the team's PaymentState (the swap/reprice path,
            // which needs it for the paid-past-deposit promotion) pass it through to avoid a
            // second ledger read. Otherwise resolve it here — but only when proc will actually
            // be computed: Empty for a brand-new team, else from the ledger.
            var billableBase = BillableProcBase(ctx, fullPayment, team.FeeBase ?? 0m, deposit, balanceDue);
            state = billableBase > 0m
                ? (isNew
                    ? PaymentState.Empty(
                        bAddProcessingFees: true,
                        ccRate: ctx.ProcessingFeePercent,
                        echeckRate: await GetEffectiveEcheckProcessingRateAsync(jobId, ct))
                    : await _paymentState.ForTeamAsync(team.TeamId, jobId, ct))
                : PaymentState.Empty(false, 0m, 0m); // proc not billable — stamp writes 0 either way
        }

        StampTeamProcessingAndTotals(team, deposit, balanceDue, ctx, fullPayment, state, rawDeposit);
    }

    /// <summary>
    /// Phase (resolved per-scope by the caller) + ApplyProcessingFeesToDeposit decide which
    /// slice of the principal counts as the "billable base" for proc.
    /// </summary>
    private static decimal BillableProcBase(
        TeamFeeApplicationContext ctx, bool fullPayment, decimal feeBase, decimal deposit, decimal balanceDue)
    {
        if (!ctx.AddProcessingFees) return 0m;
        return fullPayment
            ? (ctx.ApplyProcessingFeesToDeposit ? feeBase : balanceDue)
            : (ctx.ApplyProcessingFeesToDeposit ? deposit : 0m);
    }

    private static void StampTeamProcessingAndTotals(
        TeamsEntity team, decimal deposit, decimal balanceDue,
        TeamFeeApplicationContext ctx, bool fullPayment, PaymentState state, decimal rawDeposit)
    {
        // TotalDiscount() — the proc basis must net the same discount FeeMath subtracts from FeeTotal,
        // or the proc target disagrees with the total it is a component of.
        var billableBase = BillableProcBase(ctx, fullPayment, team.FeeBase ?? 0m, deposit, balanceDue);

        // Read the OLD totals before writing: FeeBase was re-stamped above, but
        // FeeProcessing/PaidTotal/OwedTotal still hold the pre-reprice books the split needs.
        team.FeeProcessing = billableBase > 0m
            ? TeamFeeProcessingTarget(team, rawDeposit, ctx, billableBase, state.CcRate)
            : 0m;

        team.RecalcTotals();
    }

    /// <summary>
    /// Entity-anchored FeeProcessing for a team: proc collected so far + rate on the
    /// billable principal still unpaid under the NEW structure. Same slice-aware shape as
    /// PaymentState.FeeProcessingTarget — the proc-free (deposit) slice fills first, only
    /// the spillover is billable — with two anchors swapped in: the paid split comes from
    /// the stored totals (StoredTotalsMath, never the ledger decode), and the proc-free
    /// base is the TARGET deposit being stamped (the structure the money will be billed
    /// under), not the hydrated historical one.
    ///
    /// The slice is built on the RAW configured deposit (<paramref name="rawDeposit"/>),
    /// never the EffectiveDeposit fallback: a deposit-less fee (Deposit NULL → Effective
    /// falls back to BalanceDue) has NO proc-free leg — treating the whole bill as one
    /// would drop the paid principal out of the billable base and re-project full proc on
    /// top of collected (population sweep 2026-08-11: 54 settled deposit-less teams,
    /// $59.50 each on the Big Dawgs College Club shape).
    /// </summary>
    private static decimal TeamFeeProcessingTarget(
        TeamsEntity team, decimal rawDeposit, TeamFeeApplicationContext ctx, decimal billableBase, decimal ccRate)
    {
        var (procCollected, principalPaid) = StoredTotalsMath.Split(
            team.PaidTotal ?? 0m, team.OwedTotal ?? 0m, team.FeeProcessing ?? 0m,
            ccRate, bAddProcessingFees: true); // billableBase > 0 ⇒ ctx.AddProcessingFees

        var procFreeBase = ctx.ApplyProcessingFeesToDeposit || rawDeposit <= 0m
            ? 0m
            : Math.Max(0m, rawDeposit - team.TotalDiscount() + (team.FeeLatefee ?? 0m) + (team.FeeDonation ?? 0m));

        // ProcFreeBase > 0 ⇒ the modifiers ride the proc-free (deposit) slice — they are
        // already netted inside it, so the billable base passes through raw and paid
        // principal fills the free slice FIRST. ProcFreeBase = 0 ⇒ legacy netting.
        var principalBase = procFreeBase > 0m
            ? billableBase
            : billableBase - team.TotalDiscount() + (team.FeeLatefee ?? 0m) + (team.FeeDonation ?? 0m);
        var billablePrincipalPaid = procFreeBase > 0m
            ? Math.Max(0m, principalPaid - procFreeBase)
            : principalPaid;
        var remaining = Math.Max(0m, principalBase - billablePrincipalPaid);
        return Math.Round(procCollected + remaining * ccRate, 2, MidpointRounding.AwayFromZero);
    }
}
