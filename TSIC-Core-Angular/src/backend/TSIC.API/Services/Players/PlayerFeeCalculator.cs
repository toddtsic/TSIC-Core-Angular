using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Players;

/// <summary>
/// Centralized fee calculation: FeeTotal = FeeBase + Processing - Discount - Donation.
/// Defaults Discount/Donation to 0 when null. Processing uses configured percent unless overridden.
/// </summary>
public class PlayerFeeCalculator : IPlayerFeeCalculator
{
    public (decimal ProcessingFee, decimal FeeTotal) ComputeTotals(decimal feeBase, decimal? feeDiscount = null, decimal? feeDonation = null, decimal? feeProcessingOverride = null)
    {
        if (feeBase < 0m) feeBase = 0m;
        var discount = (feeDiscount ?? 0m) < 0m ? 0m : feeDiscount ?? 0m;
        var donation = (feeDonation ?? 0m) < 0m ? 0m : feeDonation ?? 0m;
        decimal processing = feeProcessingOverride.HasValue ? feeProcessingOverride.Value : GetDefaultProcessing(feeBase);
        if (processing < 0m) processing = 0m;

        var total = feeBase + processing - discount - donation;
        if (total < 0m) total = 0m;
        return (processing, total);
    }

    public decimal GetDefaultProcessing(decimal feeBase)
        => GetDefaultProcessing(feeBase, FeeConstants.MinProcessingFeePercent / 100m);

    public decimal GetDefaultProcessing(decimal feeBase, decimal rate)
    {
        if (feeBase <= 0m) return 0m;
        return decimal.Round(feeBase * rate, 2, MidpointRounding.AwayFromZero);
    }
}
