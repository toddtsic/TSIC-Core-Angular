using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TeamsEntity = TSIC.Domain.Entities.Teams;

namespace TSIC.API.Services.Payments;

/// <summary>
/// Handles proportional adjustment of processing fees when registration amounts change.
/// Two flavors:
///   - Full CC credit (mail-in check, discount, correction): reduces by adjustmentAmount × CC_rate.
///   - Partial CC credit (eCheck): reduces by echeckAmount × (CC_rate − EC_rate), reflecting that
///     eCheck still incurs its own (lower) processing fee.
/// All paths respect Job.BAddProcessingFees. In 2-phase (deposit) scenarios, processing fees may
/// go negative (representing a credit). Otherwise, fees are clamped at zero.
/// </summary>
public interface IRegistrationFeeAdjustmentService
{
    /// <summary>
    /// Reduce processing fee by the FULL CC rate × adjustmentAmount. Use when the adjustment
    /// amount carries no replacement processing fee — mail-in check, discount, correction.
    /// Applies only if Job.BAddProcessingFees is true and processing fee exists.
    /// </summary>
    /// <param name="registration">Registration entity to adjust (modified in-place)</param>
    /// <param name="adjustmentAmount">Amount being subtracted from registration total (discount, correction, mail-in check)</param>
    /// <param name="jobId">Job to fetch BAddProcessingFees flag and fee percentage</param>
    /// <param name="userId">User applying adjustment (for audit trail)</param>
    /// <returns>Actual amount by which processing fee was reduced</returns>
    Task<decimal> ReduceProcessingFeeProportionalAsync(
        Registrations registration,
        decimal adjustmentAmount,
        Guid jobId,
        string userId);

    /// <summary>
    /// Reduce processing fee by (CC_rate − EC_rate) × echeckAmount. Use when the customer
    /// pays via eCheck — they still incur the eCheck rate, so the credit is the DIFFERENCE
    /// between the CC rate (built into FeeProcessing originally) and the EC rate they're now paying.
    /// Applies only if Job.BAddProcessingFees is true and processing fee exists.
    /// </summary>
    /// <param name="registration">Registration entity to adjust (modified in-place)</param>
    /// <param name="echeckAmount">eCheck amount processed (base payment, not including processing fees)</param>
    /// <param name="jobId">Job to fetch BAddProcessingFees flag and both rates</param>
    /// <param name="userId">User applying adjustment (for audit trail)</param>
    /// <returns>Actual amount by which processing fee was reduced</returns>
    Task<decimal> ReduceProcessingFeeForEcheckAsync(
        Registrations registration,
        decimal echeckAmount,
        Guid jobId,
        string userId);

    /// <summary>
    /// Team mirror of <see cref="ReduceProcessingFeeProportionalAsync"/>. Full CC credit.
    /// </summary>
    /// <param name="team">Team entity to adjust (modified in-place)</param>
    /// <param name="adjustmentAmount">Amount being subtracted from team total (discount, correction, mail-in check)</param>
    /// <param name="jobId">Job to fetch BAddProcessingFees flag and fee percentage</param>
    /// <param name="userId">User applying adjustment (for audit trail)</param>
    /// <returns>Actual amount by which processing fee was reduced</returns>
    Task<decimal> ReduceTeamProcessingFeeProportionalAsync(
        TeamsEntity team,
        decimal adjustmentAmount,
        Guid jobId,
        string userId);

    /// <summary>
    /// Team mirror of <see cref="ReduceProcessingFeeForEcheckAsync"/>. Partial CC credit (CC − EC).
    /// </summary>
    /// <param name="team">Team entity to adjust (modified in-place)</param>
    /// <param name="echeckAmount">eCheck amount processed (base payment)</param>
    /// <param name="jobId">Job to fetch BAddProcessingFees flag and both rates</param>
    /// <param name="userId">User applying adjustment (for audit trail)</param>
    /// <returns>Actual amount by which processing fee was reduced</returns>
    Task<decimal> ReduceTeamProcessingFeeForEcheckAsync(
        TeamsEntity team,
        decimal echeckAmount,
        Guid jobId,
        string userId);
}

public class RegistrationFeeAdjustmentService : IRegistrationFeeAdjustmentService
{
    private readonly IJobRepository _jobRepo;
    private readonly IFeeResolutionService _feeService;

    public RegistrationFeeAdjustmentService(IJobRepository jobRepo, IFeeResolutionService feeService)
    {
        _jobRepo = jobRepo;
        _feeService = feeService;
    }

    public async Task<decimal> ReduceProcessingFeeProportionalAsync(
        Registrations registration,
        decimal adjustmentAmount,
        Guid jobId,
        string userId)
    {
        if (registration == null)
            return 0m;

        if (adjustmentAmount <= 0m)
            return 0m;

        // Guard 1: Check if job is configured to add processing fees
        var feeSettings = await _jobRepo.GetJobFeeSettingsAsync(jobId);
        if (feeSettings == null || !(feeSettings.BAddProcessingFees ?? false))
            return 0m;

        // Guard 2: Check if registration has processing fees to reduce
        if (registration.FeeProcessing <= 0m)
            return 0m;

        // Get effective CC rate (ready-to-multiply decimal, e.g. 0.035 for 3.5%)
        var rate = await _feeService.GetEffectiveProcessingRateAsync(jobId);

        // Proportional reduction: adjustmentAmount × rate
        // The adjustment amount is the raw check/correction amount entered by the director
        // (base payment, not including processing fees), so straight multiplication is correct.
        var reduction = adjustmentAmount * rate;
        reduction = Math.Round(reduction, 2, MidpointRounding.AwayFromZero);

        // Guard 3: In 2-phase (deposit) scenarios, allow negative processing fees (credit).
        // Otherwise, cap reduction at current processing fee (never go negative).
        var isDepositScenario = await IsDepositScenarioAsync();
        var actualReduction = isDepositScenario ? reduction : Math.Min(reduction, registration.FeeProcessing);

        if (actualReduction > 0m || isDepositScenario)
        {
            registration.FeeProcessing -= actualReduction;
            registration.OwedTotal = Math.Max(0m, registration.OwedTotal - actualReduction);
            registration.Modified = DateTime.UtcNow;
            registration.LebUserId = userId;
        }

        return actualReduction;
    }

    public async Task<decimal> ReduceProcessingFeeForEcheckAsync(
        Registrations registration,
        decimal echeckAmount,
        Guid jobId,
        string userId)
    {
        if (registration == null)
            return 0m;

        if (echeckAmount <= 0m)
            return 0m;

        // Guard 1: Check if job is configured to add processing fees
        var feeSettings = await _jobRepo.GetJobFeeSettingsAsync(jobId);
        if (feeSettings == null || !(feeSettings.BAddProcessingFees ?? false))
            return 0m;

        // Guard 2: Check if registration has processing fees to reduce
        if (registration.FeeProcessing <= 0m)
            return 0m;

        // Get both effective rates (ready-to-multiply decimals, e.g. 0.035 and 0.015)
        var ccRate = await _feeService.GetEffectiveProcessingRateAsync(jobId);
        var ecRate = await _feeService.GetEffectiveEcheckProcessingRateAsync(jobId);

        // Partial credit: customer pays EC instead of CC, so refund the diff
        var rateDiff = ccRate - ecRate;
        if (rateDiff <= 0m)
            return 0m;

        var reduction = echeckAmount * rateDiff;
        reduction = Math.Round(reduction, 2, MidpointRounding.AwayFromZero);

        // Same deposit-scenario semantics as the mail-in path
        var isDepositScenario = await IsDepositScenarioAsync();
        var actualReduction = isDepositScenario ? reduction : Math.Min(reduction, registration.FeeProcessing);

        if (actualReduction > 0m || isDepositScenario)
        {
            registration.FeeProcessing -= actualReduction;
            registration.OwedTotal = Math.Max(0m, registration.OwedTotal - actualReduction);
            registration.Modified = DateTime.UtcNow;
            registration.LebUserId = userId;
        }

        return actualReduction;
    }

    private static async Task<bool> IsDepositScenarioAsync()
    {
        // For now, return false since we don't have team lookup in this service
        // The team discount flow doesn't use this check - it has its own deposit detection
        return await Task.FromResult(false);
    }

    public async Task<decimal> ReduceTeamProcessingFeeProportionalAsync(
        TeamsEntity team,
        decimal adjustmentAmount,
        Guid jobId,
        string userId)
    {
        if (team == null)
            return 0m;

        if (adjustmentAmount <= 0m)
            return 0m;

        // Guard 1: Check if job is configured to add processing fees
        var feeSettings = await _jobRepo.GetJobFeeSettingsAsync(jobId);
        if (feeSettings == null || !(feeSettings.BAddProcessingFees ?? false))
            return 0m;

        // Guard 2: Check if team has processing fees to reduce
        if ((team.FeeProcessing ?? 0m) <= 0m)
            return 0m;

        // Get effective CC rate (ready-to-multiply decimal, e.g. 0.035 for 3.5%)
        var rate = await _feeService.GetEffectiveProcessingRateAsync(jobId);

        // Proportional reduction: adjustmentAmount × rate
        // The adjustment amount is the raw check/correction amount entered by the director
        // (base payment, not including processing fees), so straight multiplication is correct.
        var reduction = adjustmentAmount * rate;
        reduction = Math.Round(reduction, 2, MidpointRounding.AwayFromZero);

        // Guard 3: In 2-phase (deposit) scenarios, allow negative processing fees (credit).
        // Otherwise, cap reduction at current processing fee (never go negative).
        var isDepositScenario = IsTeamDepositScenario(team);
        var currentFee = team.FeeProcessing ?? 0m;
        var actualReduction = isDepositScenario ? reduction : Math.Min(reduction, currentFee);

        if (actualReduction > 0m || isDepositScenario)
        {
            team.FeeProcessing = currentFee - actualReduction;
            team.OwedTotal = Math.Max(0m, (team.OwedTotal ?? 0m) - actualReduction);
            team.Modified = DateTime.UtcNow;
            team.LebUserId = userId;
        }

        return actualReduction;
    }

    public async Task<decimal> ReduceTeamProcessingFeeForEcheckAsync(
        TeamsEntity team,
        decimal echeckAmount,
        Guid jobId,
        string userId)
    {
        if (team == null)
            return 0m;

        if (echeckAmount <= 0m)
            return 0m;

        // Guard 1: Check if job is configured to add processing fees
        var feeSettings = await _jobRepo.GetJobFeeSettingsAsync(jobId);
        if (feeSettings == null || !(feeSettings.BAddProcessingFees ?? false))
            return 0m;

        // Guard 2: Check if team has processing fees to reduce
        if ((team.FeeProcessing ?? 0m) <= 0m)
            return 0m;

        // Get both effective rates
        var ccRate = await _feeService.GetEffectiveProcessingRateAsync(jobId);
        var ecRate = await _feeService.GetEffectiveEcheckProcessingRateAsync(jobId);

        // Partial credit: customer pays EC instead of CC, so refund the diff
        var rateDiff = ccRate - ecRate;
        if (rateDiff <= 0m)
            return 0m;

        var reduction = echeckAmount * rateDiff;
        reduction = Math.Round(reduction, 2, MidpointRounding.AwayFromZero);

        // Same deposit-scenario semantics as the mail-in team path
        var isDepositScenario = IsTeamDepositScenario(team);
        var currentFee = team.FeeProcessing ?? 0m;
        var actualReduction = isDepositScenario ? reduction : Math.Min(reduction, currentFee);

        if (actualReduction > 0m || isDepositScenario)
        {
            team.FeeProcessing = currentFee - actualReduction;
            team.OwedTotal = Math.Max(0m, (team.OwedTotal ?? 0m) - actualReduction);
            team.Modified = DateTime.UtcNow;
            team.LebUserId = userId;
        }

        return actualReduction;
    }

    private static bool IsTeamDepositScenario(TeamsEntity team)
    {
        // Team has deposit scenario if deposit is configured and balance is owed
        return (team.PerRegistrantDeposit ?? 0m) > 0m && (team.OwedTotal ?? 0m) > 0m;
    }
}
