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
        var raw = jobPercent ?? FeeConstants.MinProcessingFeePercent;
        return Math.Clamp(raw, FeeConstants.MinProcessingFeePercent, FeeConstants.MaxProcessingFeePercent) / 100m;
    }

    public async Task<decimal> GetEffectiveEcheckProcessingRateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobPercent = await _jobRepo.GetEcprocessingFeePercentAsync(jobId, ct);
        var raw = jobPercent ?? FeeConstants.MinEcprocessingFeePercent;
        return Math.Clamp(raw, FeeConstants.MinEcprocessingFeePercent, FeeConstants.MaxEcprocessingFeePercent) / 100m;
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
                .Where(m => m.ModifierType == FeeConstants.ModifierDiscount || m.ModifierType == FeeConstants.ModifierEarlyBird)
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
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;

        // Evaluate job-level modifiers only (no agegroup/team)
        var modifiers = await _feeRepo.GetActiveModifiersForJobLevelAsync(
            jobId, roleId, DateTime.Now, ct);

        var totalDiscount = modifiers
            .Where(m => m.ModifierType == FeeConstants.ModifierDiscount || m.ModifierType == FeeConstants.ModifierEarlyBird)
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
    /// Initial fee stamp at team-reservation time. Phase is driven by
    /// ctx.IsFullPaymentRequired (sourced from Jobs.BPlayersFullPaymentRequired):
    /// deposit phase → FeeBase = Deposit (or BalanceDue when no deposit configured);
    /// full-payment phase → FeeBase = Deposit + BalanceDue.
    /// </summary>
    public async Task ApplyNewRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, agegroupId, teamId, ct);
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        decimal baseFee;
        if (ctx.IsFullPaymentRequired)
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

    // ── Player Registration: Swap ───────────────────────────────

    public async Task ApplySwapFeesAsync(
        Registrations reg, Guid jobId, Guid targetAgegroupId, Guid targetTeamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, targetAgegroupId, targetTeamId, ct);
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        if (ctx.IsFullPaymentRequired)
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

        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        decimal feeBase;
        if (ctx.IsFullPaymentRequired)
        {
            feeBase = deposit + balanceDue;
        }
        else
        {
            feeBase = deposit;
        }

        var modifiers = await EvaluateModifiersAsync(
            jobId, RoleConstants.ClubRep, agegroupId, team.TeamId, DateTime.Now, ct);

        team.FeeBase = feeBase;
        team.FeeDiscount = modifiers.TotalDiscount;
        team.FeeLatefee = modifiers.TotalLateFee;

        await ApplyTeamProcessingAndTotalsAsync(team, jobId, deposit, balanceDue, ctx, isNew: true, ct);
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

        decimal feeBase;
        if (ctx.IsFullPaymentRequired)
        {
            feeBase = deposit + balanceDue;
        }
        else
        {
            feeBase = deposit;
        }

        // Only FeeBase changes — modifiers FROZEN
        team.FeeBase = feeBase;

        await ApplyTeamProcessingAndTotalsAsync(team, jobId, deposit, balanceDue, ctx, isNew: false, ct);
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
            ? Math.Round(state.FeeProcessingTarget(reg.FeeBase, reg.FeeDiscount, reg.FeeLatefee),
                2, MidpointRounding.AwayFromZero)
            : 0m;

        reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
    }

    private async Task ApplyTeamProcessingAndTotalsAsync(
        TeamsEntity team, Guid jobId, decimal deposit, decimal balanceDue,
        TeamFeeApplicationContext ctx, bool isNew, CancellationToken ct)
    {
        var feeBase = team.FeeBase ?? 0m;
        var discount = team.FeeDiscount ?? 0m;
        var lateFee = team.FeeLatefee ?? 0m;

        decimal feeProcessing = 0m;
        if (ctx.AddProcessingFees)
        {
            // Phase + ApplyProcessingFeesToDeposit decide which slice of the
            // principal counts as the "billable base" for proc calculation.
            decimal billableBase;
            if (ctx.IsFullPaymentRequired)
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
                    state.FeeProcessingTarget(billableBase, discount, lateFee),
                    2, MidpointRounding.AwayFromZero);
            }
        }

        team.FeeProcessing = feeProcessing;

        team.FeeTotal = (team.FeeBase ?? 0m)
                      + (team.FeeProcessing ?? 0m)
                      - (team.FeeDiscount ?? 0m)
                      + (team.FeeDonation ?? 0m)
                      + (team.FeeLatefee ?? 0m);
        team.OwedTotal = (team.FeeTotal ?? 0m) - (team.PaidTotal ?? 0m);
    }
}
