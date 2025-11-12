using Microsoft.Extensions.Configuration;

namespace TSIC.API.Services;

/// <summary>
/// Centralized fee calculation: FeeTotal = FeeBase + Processing - Discount - Donation.
/// Defaults Discount/Donation to 0 when null. Processing uses configured percent unless overridden.
/// </summary>
public class FeeCalculatorService : IFeeCalculatorService
{
    private readonly decimal _ccPercent;

    public FeeCalculatorService(IConfiguration config)
    {
        // Expect config path: Fees:CreditCardPercent; fallback 0.035 (3.5%)
        var raw = config["Fees:CreditCardPercent"];
        if (!decimal.TryParse(raw, out _ccPercent))
        {
            _ccPercent = 0.035m; // safe default
        }
    }

    public (decimal ProcessingFee, decimal FeeTotal) ComputeTotals(decimal feeBase, decimal? feeDiscount = null, decimal? feeDonation = null, decimal? feeProcessingOverride = null)
    {
        if (feeBase < 0m) feeBase = 0m; // normalize
        var discount = (feeDiscount ?? 0m) < 0m ? 0m : feeDiscount ?? 0m;
        var donation = (feeDonation ?? 0m) < 0m ? 0m : feeDonation ?? 0m;
        decimal processing = feeProcessingOverride.HasValue ? feeProcessingOverride.Value : GetDefaultProcessing(feeBase);
        // Guard negative from extreme overrides
        if (processing < 0m) processing = 0m;

        var total = feeBase + processing - discount - donation;
        if (total < 0m) total = 0m; // never negative
        return (processing, total);
    }

    public decimal GetDefaultProcessing(decimal feeBase)
    {
        if (feeBase <= 0m) return 0m;
        // Round to cents (bankers rounding avoided; standard midpoint away from zero)
        var raw = feeBase * _ccPercent;
        return decimal.Round(raw, 2, MidpointRounding.AwayFromZero);
    }
}
