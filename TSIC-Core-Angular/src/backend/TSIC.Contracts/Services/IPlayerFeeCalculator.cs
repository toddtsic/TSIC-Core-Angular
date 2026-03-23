namespace TSIC.Contracts.Services;

/// <summary>
/// Consolidated player fee calculation.
/// Computes processing fees and totals from base fee inputs.
/// Single interface replacing both Application.IPlayerFeeCalculator and API.IRegistrationRecordFeeCalculatorService.
/// </summary>
public interface IPlayerFeeCalculator
{
    /// <summary>
    /// Returns (processingFee, feeTotal) for given inputs.
    /// Processing = feeBase * CC% unless overridden.
    /// Total = feeBase + processing - discount - donation.
    /// </summary>
    (decimal ProcessingFee, decimal FeeTotal) ComputeTotals(
        decimal feeBase,
        decimal? feeDiscount = null,
        decimal? feeDonation = null,
        decimal? feeProcessingOverride = null);

    /// <summary>
    /// Returns the default processing fee (feeBase * CC%) rounded to cents.
    /// Uses the system minimum rate (3.5%).
    /// </summary>
    decimal GetDefaultProcessing(decimal feeBase);

    /// <summary>
    /// Returns the processing fee for a given base and explicit rate (decimal multiplier, e.g. 0.035).
    /// </summary>
    decimal GetDefaultProcessing(decimal feeBase, decimal rate);
}
