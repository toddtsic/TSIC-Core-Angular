using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

using TeamsEntity = TSIC.Domain.Entities.Teams;

namespace TSIC.API.Services.Fees;

/// <summary>
/// Single source of truth for fee resolution and application.
/// Reads from fees.JobFees via cascade, stamps results onto Registration/Team snapshot fields.
/// Replaces PlayerRegistrationFeeService and TeamFeeCalculator.
/// </summary>
public sealed class FeeResolutionService : IFeeResolutionService
{
    private readonly IFeeRepository _feeRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IPlayerFeeCalculator _playerFeeCalc;

    public FeeResolutionService(
        IFeeRepository feeRepo,
        IJobRepository jobRepo,
        IPlayerFeeCalculator playerFeeCalc)
    {
        _feeRepo = feeRepo;
        _jobRepo = jobRepo;
        _playerFeeCalc = playerFeeCalc;
    }

    // ── Processing Fee Rate ─────────────────────────────────────

    public async Task<decimal> GetEffectiveProcessingRateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobPercent = await _jobRepo.GetProcessingFeePercentAsync(jobId, ct);
        return Math.Max(jobPercent ?? FeeConstants.MinProcessingFeePercent, FeeConstants.MinProcessingFeePercent) / 100m;
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
                .Where(m => m.ModifierType == "Discount" || m.ModifierType == "EarlyBird")
                .Sum(m => m.Amount),
            TotalLateFee = modifiers
                .Where(m => m.ModifierType == "LateFee")
                .Sum(m => m.Amount)
        };
    }

    // ── Player Registration: New ────────────────────────────────

    public async Task ApplyNewRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, agegroupId, teamId, ct);
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;

        var modifiers = await EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agegroupId, teamId, DateTime.UtcNow, ct);

        reg.FeeBase = baseFee;
        reg.FeeDiscount = modifiers.TotalDiscount;
        reg.FeeLatefee = modifiers.TotalLateFee;
        // FeeDonation — not set here; player sets it in wizard

        var rate = await GetEffectiveProcessingRateAsync(jobId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate);
    }

    // ── Player Registration: Swap ───────────────────────────────

    public async Task ApplySwapFeesAsync(
        Registrations reg, Guid jobId, Guid targetAgegroupId, Guid targetTeamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, targetAgegroupId, targetTeamId, ct);
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;

        // Only FeeBase changes — modifiers are FROZEN from original registration
        reg.FeeBase = baseFee;
        // FeeDiscount — KEPT
        // FeeLatefee  — KEPT
        // FeeDonation — KEPT

        var rate = await GetEffectiveProcessingRateAsync(jobId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate);
    }

    // ── Team Entity: New ────────────────────────────────────────

    public async Task ApplyNewTeamFeesAsync(
        TeamsEntity team, Guid jobId, Guid agegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeForAgegroupAsync(jobId, RoleConstants.ClubRep, agegroupId, ct);

        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        // Phase determines FeeBase
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
            jobId, RoleConstants.ClubRep, agegroupId, team.TeamId, DateTime.UtcNow, ct);

        team.FeeBase = feeBase;
        team.FeeDiscount = modifiers.TotalDiscount;
        team.FeeLatefee = modifiers.TotalLateFee;

        ApplyTeamProcessingAndTotals(team, feeBase, deposit, balanceDue, ctx);
    }

    // ── Team Entity: Swap ───────────────────────────────────────

    public async Task ApplyTeamSwapFeesAsync(
        TeamsEntity team, Guid jobId, Guid targetAgegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeForAgegroupAsync(jobId, RoleConstants.ClubRep, targetAgegroupId, ct);

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

        ApplyTeamProcessingAndTotals(team, feeBase, deposit, balanceDue, ctx);
    }

    // ── Private: Processing + Totals ────────────────────────────

    private void ApplyProcessingAndTotals(Registrations reg, FeeApplicationContext ctx, decimal processingRate)
    {
        if (ctx.AddProcessingFees && reg.FeeBase > 0m)
        {
            var adjustedBase = Math.Max(reg.FeeBase - ctx.NonCcPayments, 0m);
            reg.FeeProcessing = _playerFeeCalc.GetDefaultProcessing(adjustedBase, processingRate);
        }
        else
        {
            reg.FeeProcessing = 0m;
        }

        reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
    }

    private static void ApplyTeamProcessingAndTotals(
        TeamsEntity team, decimal feeBase, decimal deposit, decimal balanceDue,
        TeamFeeApplicationContext ctx)
    {
        decimal feeProcessing = 0m;
        if (ctx.AddProcessingFees)
        {
            var percent = ctx.ProcessingFeePercent;
            if (ctx.IsFullPaymentRequired)
            {
                feeProcessing = ctx.ApplyProcessingFeesToDeposit
                    ? feeBase * percent          // full amount
                    : balanceDue * percent;       // balance-due only
            }
            else
            {
                feeProcessing = ctx.ApplyProcessingFeesToDeposit
                    ? deposit * percent           // deposit
                    : 0m;                         // no processing in deposit phase
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
