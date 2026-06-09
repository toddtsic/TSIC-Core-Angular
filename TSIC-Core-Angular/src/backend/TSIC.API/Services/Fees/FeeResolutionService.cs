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
        DateTime asOfDate,
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
        if (resolved is not { FeeConfigured: true })
            throw new FeeNotConfiguredException(jobId, RoleConstants.Staff, agegroupId, teamId);
        var baseFee = resolved.EffectiveBalanceDue;

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
        if (resolved is not { FeeConfigured: true })
            throw new FeeNotConfiguredException(jobId, roleId, null, null);
        var baseFee = resolved.EffectiveBalanceDue;

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
            baseFee = deposit + balanceDue;
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
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        reg.FeeBase = deposit + balanceDue;
        // FeeDiscount / FeeLatefee / FeeDonation preserved from initial stamp

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

        // Phase follows the TARGET scope (team → ag → league override) ?? job baseline.
        if (ResolvedFee.ResolveFullPaymentPhase(resolved, ctx.IsFullPaymentRequired))
        {
            reg.FeeBase = deposit + balanceDue;
        }
        else
        {
            reg.FeeBase = deposit > 0m ? deposit : balanceDue;
        }
        // FeeDiscount / FeeLatefee / FeeDonation preserved

        await ApplyRegistrationProcessingAndTotalsAsync(reg, jobId, isNew: false, ct);
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
        var feeBase = fullPayment ? deposit + balanceDue : deposit;

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

        // Phase follows the TARGET scope override ?? job baseline (ctx).
        var fullPayment = ResolvedFee.ResolveFullPaymentPhase(resolved, ctx.IsFullPaymentRequired);
        var feeBase = fullPayment ? deposit + balanceDue : deposit;

        // Only FeeBase changes — modifiers FROZEN
        team.FeeBase = feeBase;

        await ApplyTeamProcessingAndTotalsAsync(team, jobId, deposit, balanceDue, ctx, fullPayment, isNew: false, ct);
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
        Registrations reg, Guid jobId, bool isNew, CancellationToken ct)
    {
        var state = isNew
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
        TeamFeeApplicationContext ctx, bool fullPayment, bool isNew, CancellationToken ct)
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
                var state = isNew
                    ? PaymentState.Empty(
                        bAddProcessingFees: true,
                        ccRate: ctx.ProcessingFeePercent,
                        echeckRate: await GetEffectiveEcheckProcessingRateAsync(jobId, ct))
                    : await _paymentState.ForTeamAsync(team.TeamId, jobId, ct);

                feeProcessing = Math.Round(
                    state.FeeProcessingTarget(billableBase, discount, lateFee, donation),
                    2, MidpointRounding.AwayFromZero);
            }
        }

        team.FeeProcessing = feeProcessing;

        team.RecalcTotals();
    }
}
