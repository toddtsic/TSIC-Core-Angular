using Microsoft.Extensions.Configuration;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Players;

/// <summary>
/// Centralized fee calculation: FeeTotal = FeeBase + Processing - Discount - Donation.
/// Defaults Discount/Donation to 0 when null. Processing uses configured percent unless overridden.
/// </summary>
public class PlayerFeeCalculator : IPlayerFeeCalculator
{
    private readonly decimal _ccPercent;

    public PlayerFeeCalculator(IConfiguration config)
    {
        var raw = config["Fees:CreditCardPercent"];
        if (!decimal.TryParse(raw, out _ccPercent))
        {
            _ccPercent = 0.035m; // safe default 3.5%
        }
    }

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
    {
        if (feeBase <= 0m) return 0m;
        var raw = feeBase * _ccPercent;
        return decimal.Round(raw, 2, MidpointRounding.AwayFromZero);
    }
}
