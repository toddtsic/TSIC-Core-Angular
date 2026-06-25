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
        PaymentState state, decimal fullPrice, decimal discount, decimal donation,
        CancellationToken ct)
    {
        // Sequential awaits (shared scoped DbContext) — never Task.WhenAll.
        var windowed = (await EvaluateModifiersAsync(jobId, roleId, agegroupId, teamId, DateTime.Now, ct)).TotalLateFee;
        var configured = (await EvaluateModifiersAsync(jobId, roleId, agegroupId, teamId, null, ct)).TotalLateFee;
        return state.EffectiveLateFee(windowed, configured, fullPrice, discount, donation);
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
    /// (team → agegroup → league) wins, else ctx.IsFullPaymentRequired (the job-level
    /// baseline the caller passes). Deposit phase → FeeBase = Deposit (or BalanceDue
    /// when no deposit configured); full-payment phase → FeeBase = Deposit + BalanceDue.
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
        if (ResolvedFee.ResolveFullPaymentPhase(resolved, ctx.IsFullPaymentRequired))
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
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        // Phase is decided from BOTH the config cascade AND the registrant's own payments:
        //   (1) Config: per-scope override (team → ag → league) ?? job baseline.
        //   (2) Promotion: having paid PAST the deposit tier IS entering full payment. The
        //       registrant's payment history overrides the scope's deposit-phase default, so a
        //       fee/phase change can never re-stamp a paid-ahead reg DOWN to the deposit (which
        //       would net a bogus credit), AND a price increase correctly reaches already-paid
        //       registrants — they owe the delta.
        // The threshold is principal-based (proc backed out per method via PaymentState) compared
        // against the discount/late/donation-adjusted deposit, with a small tolerance so a reg
        // that paid EXACTLY its deposit is not spuriously promoted. The same PaymentState is
        // reused for the totals recompute below (one ledger read, not two).
        const decimal depositPaidTolerance = 0.01m;
        var state = await _paymentState.ForRegistrationAsync(reg.RegistrationId, jobId, ct);
        var effectiveDeposit = Math.Max(0m, deposit - reg.FeeDiscount + reg.FeeLatefee + reg.FeeDonation);
        var paidPastDeposit = state.PrincipalPaid > effectiveDeposit + depositPaidTolerance;

        var fullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved, ctx.IsFullPaymentRequired) || paidPastDeposit;
        reg.FeeBase = fullPayment
            ? (resolved?.FullPrice ?? 0m)
            : (deposit > 0m ? deposit : balanceDue);
        // FeeDiscount / FeeLatefee / FeeDonation preserved

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
                state, resolved?.FullPrice ?? 0m, reg.FeeDiscount, reg.FeeDonation, ct);
        }

        await ApplyRegistrationProcessingAndTotalsAsync(reg, jobId, isNew: false, ct, state);
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

        // Per-scope override (team → ag → league) ?? job baseline (ctx). Decided ONCE
        // and threaded into the proc/totals helper so phase can never disagree there.
        var fullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved, ctx.IsFullPaymentRequired);
        var feeBase = fullPayment ? resolved.FullPrice : deposit;

        var modifiers = await EvaluateModifiersAsync(
            jobId, RoleConstants.ClubRep, agegroupId, team.TeamId, DateTime.Now, ct);

        team.FeeBase = feeBase;
        team.FeeDiscount = modifiers.TotalDiscount;
        team.FeeLatefee = modifiers.TotalLateFee;

        await ApplyTeamProcessingAndTotalsAsync(team, jobId, deposit, balanceDue, ctx, fullPayment, isNew: true, ct);
    }

    // ── Team Entity: Swap ───────────────────────────────────────

    public async Task ApplyTeamSwapFeesAsync(
        TeamsEntity team, Guid jobId, Guid targetAgegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.ClubRep, targetAgegroupId, team.TeamId, ct);

        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        // Phase is decided from BOTH the config cascade AND the team's own payments — identical to
        // ApplySwapFeesAsync (the player swap applier):
        //   (1) Config: per-scope override (team → ag → league) ?? job baseline (ctx).
        //   (2) Promotion: having paid PAST the deposit tier IS entering full payment. This makes
        //       the reprice engine's old OwedTotal<=0 skip unnecessary — a paid-ahead (or
        //       owed-zeroed) team is re-stamped to FullPrice, NEVER down to the deposit, so a
        //       PIF→deposit downgrade can't net a bogus credit, AND a deposit→PIF upgrade reaches
        //       a team whose deposit-phase owed was already zeroed (e.g. by a correction).
        // Threshold is principal-based (proc backed out per method via PaymentState), compared
        // against the discount/late/donation-adjusted deposit with a small tolerance so a team
        // that paid EXACTLY its deposit is not spuriously promoted. The same PaymentState is
        // reused for the totals recompute below (one ledger read, not two).
        const decimal depositPaidTolerance = 0.01m;
        var state = await _paymentState.ForTeamAsync(team.TeamId, jobId, ct);
        var effectiveDeposit = Math.Max(
            0m, deposit - (team.FeeDiscount ?? 0m) + (team.FeeLatefee ?? 0m) + (team.FeeDonation ?? 0m));
        var paidPastDeposit = state.PrincipalPaid > effectiveDeposit + depositPaidTolerance;

        var fullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved, ctx.IsFullPaymentRequired) || paidPastDeposit;
        var feeBase = fullPayment ? (resolved?.FullPrice ?? 0m) : deposit;

        // FeeBase changes for the phase; the late fee re-derives below (the club-rep analog of the
        // player swap path) — discount/donation stay frozen.
        team.FeeBase = feeBase;

        // Late fee: re-derive live (derived "pay late ⇒ owe more" model) when this recompute opts in
        // via ctx.AssessActiveLateFee (the director's "update all prior" reprice, and the team
        // payment recompute). EffectiveLateFee adds an in-window fee to a team that owes, holds the
        // floor for one already paid (survives the window closing), and drops/reduces it on
        // delete/edit (overpay → negative owed → refund). Pure roster moves (flag false) freeze it.
        if (ctx.AssessActiveLateFee)
        {
            team.FeeLatefee = await ResolveEffectiveLateFeeAsync(
                jobId, RoleConstants.ClubRep, targetAgegroupId, team.TeamId,
                state, resolved?.FullPrice ?? 0m, team.FeeDiscount ?? 0m, team.FeeDonation ?? 0m, ct);
        }

        await ApplyTeamProcessingAndTotalsAsync(team, jobId, deposit, balanceDue, ctx, fullPayment, isNew: false, ct, state);
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
        var baseline = await _jobRepo.GetFullPaymentBaselineAsync(jobId, ct);
        await ApplySwapFeesAsync(
            reg, jobId, agegroupId, teamId,
            new FeeApplicationContext
            {
                IsFullPaymentRequired = baseline?.BPlayersFullPaymentRequired ?? false,
                AssessActiveLateFee = true
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
                IsFullPaymentRequired = settings?.BTeamsFullPaymentRequired ?? false,
                AddProcessingFees = settings?.BAddProcessingFees ?? false,
                ApplyProcessingFeesToDeposit = settings?.BApplyProcessingFeesToTeamDeposit ?? false,
                ProcessingFeePercent = processingRate,
                AssessActiveLateFee = true
            },
            ct);
    }

    // ── Private: Processing + Totals (canonical) ────────────────
    //
    // FeeProcessing is computed from PaymentState.FeeProcessingTarget(...),
    // which encodes the invariant
    //   FeeProcessing = ProcCollected + remainingCcBillable × CcRate
    // — i.e. proc already taken at swipe (CC + eCheck) plus proc that would
    // be collected if the rest were CC-billed. Per-payment handlers maintain
    // the same invariant incrementally; recalc paths just resolve it from
    // PaymentState. Old "subtract NonCcPayments × ccRate" math is replaced —
    // it lumped eCheck (which DOES collect proc) with check/correction
    // (which don't), losing the eCheck partial credit on every recalc.

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

        reg.FeeProcessing = reg.FeeBase > 0m
            ? Math.Round(state.FeeProcessingTarget(reg.FeeBase, reg.FeeDiscount, reg.FeeLatefee, reg.FeeDonation),
                2, MidpointRounding.AwayFromZero)
            : 0m;

        reg.RecalcTotals();
    }

    private async Task ApplyTeamProcessingAndTotalsAsync(
        TeamsEntity team, Guid jobId, decimal deposit, decimal balanceDue,
        TeamFeeApplicationContext ctx, bool fullPayment, bool isNew, CancellationToken ct,
        PaymentState? state = null)
    {
        var feeBase = team.FeeBase ?? 0m;
        var discount = team.FeeDiscount ?? 0m;
        var lateFee = team.FeeLatefee ?? 0m;
        var donation = team.FeeDonation ?? 0m;

        decimal feeProcessing = 0m;
        if (ctx.AddProcessingFees)
        {
            // Phase (resolved per-scope by the caller) + ApplyProcessingFeesToDeposit
            // decide which slice of the principal counts as the "billable base" for proc.
            decimal billableBase;
            if (fullPayment)
            {
                billableBase = ctx.ApplyProcessingFeesToDeposit ? feeBase : balanceDue;
            }
            else
            {
                billableBase = ctx.ApplyProcessingFeesToDeposit ? deposit : 0m;
            }

            if (billableBase > 0m)
            {
                // Callers that already resolved the team's PaymentState (the swap/reprice path,
                // which needs it for the paid-past-deposit promotion) pass it through to avoid a
                // second ledger read. Otherwise resolve it here: Empty for a brand-new team, else
                // from the ledger.
                var procState = isNew
                    ? PaymentState.Empty(
                        bAddProcessingFees: true,
                        ccRate: ctx.ProcessingFeePercent,
                        echeckRate: await GetEffectiveEcheckProcessingRateAsync(jobId, ct))
                    : state ?? await _paymentState.ForTeamAsync(team.TeamId, jobId, ct);

                feeProcessing = Math.Round(
                    procState.FeeProcessingTarget(billableBase, discount, lateFee, donation),
                    2, MidpointRounding.AwayFromZero);
            }
        }

        team.FeeProcessing = feeProcessing;

        team.RecalcTotals();
    }
}
