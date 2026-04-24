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

    /// <summary>
    /// Returns both the effective processing rate AND whether the job has processing fees enabled.
    /// Single source of truth — callers should not need to pass AddProcessingFees externally.
    /// </summary>
    private async Task<(decimal Rate, bool Enabled)> GetProcessingConfigAsync(Guid jobId, CancellationToken ct = default)
    {
        var settings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);
        var enabled = settings?.BAddProcessingFees ?? false;
        var rate = await GetEffectiveProcessingRateAsync(jobId, ct);
        return (rate, enabled);
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

        var (rate, enabled) = await GetProcessingConfigAsync(jobId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate, enabled);
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

        var (rate, enabled) = await GetProcessingConfigAsync(jobId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate, enabled);
    }

    // ── Player Registration: New ────────────────────────────────

    /// <summary>
    /// Initial fee stamp at team-reservation time. Always defaults to the
    /// deposit phase — FeeBase = Deposit when configured, else BalanceDue.
    /// PIF is never applied here, even if the job has ALLOWPIF — that is an
    /// explicit upgrade at checkout via ApplyPifUpgradeAsync.
    /// </summary>
    public async Task ApplyNewRegistrationFeesAsync(
        Registrations reg, Guid jobId, Guid agegroupId, Guid teamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, agegroupId, teamId, ct);
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        var baseFee = deposit > 0m ? deposit : balanceDue;

        var modifiers = await EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agegroupId, teamId, DateTime.Now, ct);

        reg.FeeBase = baseFee;
        reg.FeeDiscount = modifiers.TotalDiscount;
        reg.FeeLatefee = modifiers.TotalLateFee;
        // FeeDonation — not set here; player sets it in wizard

        var (rate, enabled) = await GetProcessingConfigAsync(jobId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate, enabled);
    }

    // ── Player Registration: PIF Upgrade (checkout) ─────────────

    /// <summary>
    /// Explicit upgrade from deposit phase to Pay In Full at checkout.
    /// Re-stamps FeeBase = Deposit + BalanceDue. Modifiers are PRESERVED —
    /// caller is responsible for verifying the ALLOWPIF policy gate.
    /// </summary>
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

        var (rate, enabled) = await GetProcessingConfigAsync(jobId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate, enabled);
    }

    // ── Player Registration: Swap ───────────────────────────────

    /// <summary>
    /// Re-stamps FeeBase to the new team's deposit-phase amount
    /// (Deposit when configured, else BalanceDue). Modifiers are FROZEN
    /// from the original registration. If the player was in PIF before the
    /// swap, they will need to re-opt at checkout — the swap does not
    /// preserve PIF phase across teams.
    /// </summary>
    public async Task ApplySwapFeesAsync(
        Registrations reg, Guid jobId, Guid targetAgegroupId, Guid targetTeamId,
        FeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.Player, targetAgegroupId, targetTeamId, ct);
        var deposit = resolved?.EffectiveDeposit ?? 0m;
        var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;

        reg.FeeBase = deposit > 0m ? deposit : balanceDue;
        // FeeDiscount / FeeLatefee / FeeDonation preserved

        var (rate, enabled) = await GetProcessingConfigAsync(jobId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate, enabled);
    }

    // ── Team Entity: New ────────────────────────────────────────

    public async Task ApplyNewTeamFeesAsync(
        TeamsEntity team, Guid jobId, Guid agegroupId,
        TeamFeeApplicationContext ctx,
        CancellationToken ct = default)
    {
        // Full Team → Agegroup → Job cascade for ClubRep, mirroring the Player path.
        // Symmetric scope design — see scripts/6b verify-fees-feebase-concordance.sql
        // TEST 2, which joins team/agegroup/job rows identically to Player TEST 1.
        var resolved = await ResolveFeeAsync(jobId, RoleConstants.ClubRep, agegroupId, team.TeamId, ct);

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
            jobId, RoleConstants.ClubRep, agegroupId, team.TeamId, DateTime.Now, ct);

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
        // Full Team → Agegroup → Job cascade; honors a team-level ClubRep override if
        // one exists for this team in its new agegroup. See FeeResolutionService.ApplyNewTeamFeesAsync
        // for the design rationale.
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

        ApplyTeamProcessingAndTotals(team, feeBase, deposit, balanceDue, ctx);
    }

    // ── Private: Processing + Totals ────────────────────────────

    private void ApplyProcessingAndTotals(Registrations reg, FeeApplicationContext ctx, decimal processingRate, bool jobEnablesProcessingFees)
    {
        if (jobEnablesProcessingFees && reg.FeeBase > 0m)
        {
            // Processing % is taken on the net billable amount — discount reduces it, late fee adds to it.
            var netBase = Math.Max(reg.FeeBase - reg.FeeDiscount + reg.FeeLatefee - ctx.NonCcPayments, 0m);
            reg.FeeProcessing = _playerFeeCalc.GetDefaultProcessing(netBase, processingRate);
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
        // Processing is charged on the net billable amount. Discount reduces it; late fee adds to it.
        var discount = team.FeeDiscount ?? 0m;
        var lateFee = team.FeeLatefee ?? 0m;

        decimal feeProcessing = 0m;
        if (ctx.AddProcessingFees)
        {
            var percent = ctx.ProcessingFeePercent;
            decimal netBase;
            if (ctx.IsFullPaymentRequired)
            {
                netBase = ctx.ApplyProcessingFeesToDeposit
                    ? Math.Max(feeBase - discount + lateFee, 0m)      // full amount
                    : Math.Max(balanceDue - discount + lateFee, 0m);   // balance-due only
            }
            else
            {
                netBase = ctx.ApplyProcessingFeesToDeposit
                    ? Math.Max(deposit - discount + lateFee, 0m)       // deposit
                    : 0m;                                              // no processing in deposit phase
            }
            feeProcessing = netBase * percent;
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
