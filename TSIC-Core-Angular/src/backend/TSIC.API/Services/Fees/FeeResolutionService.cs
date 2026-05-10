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
    private readonly IRegistrationAccountingRepository _accounting;

    public FeeResolutionService(
        IFeeRepository feeRepo,
        IJobRepository jobRepo,
        IPlayerFeeCalculator playerFeeCalc,
        IRegistrationAccountingRepository accounting)
    {
        _feeRepo = feeRepo;
        _jobRepo = jobRepo;
        _playerFeeCalc = playerFeeCalc;
        _accounting = accounting;
    }

    // ── Non-CC Payment Lookups ──────────────────────────────────
    // Owned here (not by callers) so re-stamping FeeProcessing on swap/PIF/recalc
    // always subtracts the credit a non-CC payment has earned. Without this the
    // service would silently revert that credit any time FeeProcessing is recomputed.
    // For "new" entities (no payments yet) callers skip the query and pass 0m.

    private async Task<decimal> GetRegistrationNonCcAsync(Guid registrationId, CancellationToken ct)
    {
        var summaries = await _accounting.GetPaymentSummariesAsync(new[] { registrationId }, ct);
        return summaries.TryGetValue(registrationId, out var s) ? s.NonCcPayments : 0m;
    }

    private async Task<decimal> GetTeamNonCcAsync(Guid teamId, CancellationToken ct)
    {
        var totals = await _accounting.GetTeamNonCcPaymentTotalsAsync(new[] { teamId }, ct);
        return totals.GetValueOrDefault(teamId, 0m);
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
        // New registration → no payments yet, NonCcPayments = 0.
        ApplyProcessingAndTotals(reg, ctx, rate, enabled, nonCcPayments: 0m);
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
        // New registration → no payments yet, NonCcPayments = 0.
        ApplyProcessingAndTotals(reg, ctx, rate, enabled, nonCcPayments: 0m);
    }

    // ── Player Registration: New ────────────────────────────────

    /// <summary>
    /// Initial fee stamp at team-reservation time. Phase is driven by
    /// ctx.IsFullPaymentRequired (sourced from Jobs.BPlayersFullPaymentRequired):
    /// deposit phase → FeeBase = Deposit (or BalanceDue when no deposit configured);
    /// full-payment phase → FeeBase = Deposit + BalanceDue.
    /// The parent's voluntary PIF choice at checkout uses ApplyPifUpgradeAsync;
    /// this method handles the job-level default at submit time.
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
        // FeeDonation — not set here; player sets it in wizard

        var (rate, enabled) = await GetProcessingConfigAsync(jobId, ct);
        // New registration → no payments yet, NonCcPayments = 0.
        ApplyProcessingAndTotals(reg, ctx, rate, enabled, nonCcPayments: 0m);
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
        // PIF upgrade may run on a registration with prior non-CC payments — fetch.
        var nonCc = await GetRegistrationNonCcAsync(reg.RegistrationId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate, enabled, nonCc);
    }

    // ── Player Registration: Swap ───────────────────────────────

    /// <summary>
    /// Re-stamps FeeBase to the target team's resolved fee for the current phase.
    /// Phase is driven by ctx.IsFullPaymentRequired (sourced from
    /// Jobs.BPlayersFullPaymentRequired): deposit phase → FeeBase = Deposit
    /// (or BalanceDue when no deposit configured); full-payment phase →
    /// FeeBase = Deposit + BalanceDue. Modifiers are FROZEN from the original
    /// registration. Used by team swaps and the director's bulk recalc on flag flip.
    /// </summary>
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

        var (rate, enabled) = await GetProcessingConfigAsync(jobId, ct);
        // Swap may run on a registration with prior non-CC payments (roster move,
        // bulk recalc on phase flip) — fetch so the credit isn't reverted.
        var nonCc = await GetRegistrationNonCcAsync(reg.RegistrationId, ct);
        ApplyProcessingAndTotals(reg, ctx, rate, enabled, nonCc);
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

        // New team → no payments yet, NonCcPayments = 0.
        ApplyTeamProcessingAndTotals(team, feeBase, deposit, balanceDue, ctx, nonCcPayments: 0m);
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

        // Swap may run on a team with prior non-CC payments (division swap, bulk recalc
        // on BTeamsFullPaymentRequired flip) — fetch so the credit isn't reverted.
        var nonCc = await GetTeamNonCcAsync(team.TeamId, ct);
        ApplyTeamProcessingAndTotals(team, feeBase, deposit, balanceDue, ctx, nonCc);
    }

    // ── Private: Processing + Totals ────────────────────────────

    private void ApplyProcessingAndTotals(
        Registrations reg, FeeApplicationContext ctx, decimal processingRate,
        bool jobEnablesProcessingFees, decimal nonCcPayments)
    {
        if (jobEnablesProcessingFees && reg.FeeBase > 0m)
        {
            // Processing % is taken on the net billable amount — discount reduces it, late
            // fee adds to it, and prior non-CC payments have already earned a fee credit.
            var netBase = Math.Max(reg.FeeBase - reg.FeeDiscount + reg.FeeLatefee - nonCcPayments, 0m);
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
        TeamFeeApplicationContext ctx, decimal nonCcPayments)
    {
        // Processing is charged on the net billable amount. Discount reduces it; late fee
        // adds to it; prior non-CC payments (check/e-check/cash/correction) have already
        // earned a fee credit so subtracting them keeps re-stamps consistent.
        // Without this subtraction, a phase advance (BTeamsFullPaymentRequired flip)
        // re-stamps FeeProcessing as ccRate × full-principal and reverts the credit a
        // check/correction had already earned — OwedTotal jumps by exactly that credit
        // (e.g. $19 on a $500 check at 3.8%).
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
                    ? Math.Max(feeBase - discount + lateFee - nonCcPayments, 0m)      // full amount
                    : Math.Max(balanceDue - discount + lateFee - nonCcPayments, 0m);   // balance-due only
            }
            else
            {
                netBase = ctx.ApplyProcessingFeesToDeposit
                    ? Math.Max(deposit - discount + lateFee - nonCcPayments, 0m)       // deposit
                    : 0m;                                                              // no processing in deposit phase
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
