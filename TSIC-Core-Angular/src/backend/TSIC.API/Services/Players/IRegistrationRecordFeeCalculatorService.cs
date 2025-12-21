namespace TSIC.API.Services.Players;

public interface IRegistrationRecordFeeCalculatorService
{
    /// <summary>
    /// Returns (processingFee, feeTotal) for given inputs. If feeProcessingOverride is provided, it's used instead of default calculation.
    /// </summary>
    (decimal ProcessingFee, decimal FeeTotal) ComputeTotals(decimal feeBase, decimal? feeDiscount = null, decimal? feeDonation = null, decimal? feeProcessingOverride = null);

    /// <summary>
    /// Returns the default processing fee for the given base using configured CC percent.
    /// </summary>
    decimal GetDefaultProcessing(decimal feeBase);
}
