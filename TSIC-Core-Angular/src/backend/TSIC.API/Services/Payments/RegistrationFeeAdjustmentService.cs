using Microsoft.Extensions.Configuration;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.API.Services.Teams;

namespace TSIC.API.Services.Payments;

/// <summary>
/// Handles proportional adjustment of processing fees when registration amounts change.
/// Reduces processing fees by the credit card percentage of the adjustment amount,
/// respecting Job.BAddProcessingFees flag. In 2-phase (deposit) scenarios, processing
/// fees may go negative (representing a credit). Otherwise, fees are clamped at zero.
/// </summary>
public interface IRegistrationFeeAdjustmentService
{
    /// <summary>
    /// Reduce processing fee proportionally based on discount or adjustment amount.
    /// Applies only if Job.BAddProcessingFees is true and processing fee exists.
    /// </summary>
    /// <param name="registration">Registration entity to adjust (modified in-place)</param>
    /// <param name="adjustmentAmount">Amount being subtracted from registration total (discount, correction, etc.)</param>
    /// <param name="jobId">Job to fetch BAddProcessingFees flag and fee percentage</param>
    /// <param name="userId">User applying adjustment (for audit trail)</param>
    /// <returns>Actual amount by which processing fee was reduced</returns>
    Task<decimal> ReduceProcessingFeeProportionalAsync(
        Registrations registration,
        decimal adjustmentAmount,
        Guid jobId,
        string userId);
}

public class RegistrationFeeAdjustmentService : IRegistrationFeeAdjustmentService
{
    private readonly IJobRepository _jobRepo;
    private readonly IConfiguration _config;
    private readonly ITeamLookupService _teamLookup;

    public RegistrationFeeAdjustmentService(IJobRepository jobRepo, IConfiguration config, ITeamLookupService teamLookup)
    {
        _jobRepo = jobRepo;
        _config = config;
        _teamLookup = teamLookup;
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
        if (feeSettings == null || (feeSettings.BAddProcessingFees ?? false) == false)
            return 0m;

        // Guard 2: Check if registration has processing fees to reduce
        if (registration.FeeProcessing <= 0m)
            return 0m;

        // Get CC fee percentage from Job or config
        var feePercent = feeSettings.BAddProcessingFees == true
            ? await _jobRepo.GetProcessingFeePercentAsync(jobId) ?? GetDefaultProcessingPercent()
            : GetDefaultProcessingPercent();

        // Calculate proportional reduction: adjustmentAmount × CC percentage
        var reduction = adjustmentAmount * (feePercent / 100m);
        reduction = Math.Round(reduction, 2, MidpointRounding.AwayFromZero);

        // Guard 3: In 2-phase (deposit) scenarios, allow negative processing fees (credit).
        // Otherwise, cap reduction at current processing fee (never go negative).
        var isDepositScenario = await IsDepositScenarioAsync(registration);
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

    private async Task<bool> IsDepositScenarioAsync(Registrations registration)
    {
        // Deposit scenario: registration has assigned team with both fee > 0 and deposit > 0
        if (!registration.AssignedTeamId.HasValue)
            return false;

        var (fee, deposit) = await _teamLookup.ResolvePerRegistrantAsync(registration.AssignedTeamId.Value);
        return fee > 0m && deposit > 0m;
    }

    private decimal GetDefaultProcessingPercent()
    {
        var configValue = _config["Fees:CreditCardPercent"];
        return decimal.TryParse(configValue, out var pct) ? pct : 2.9m;
    }
}
