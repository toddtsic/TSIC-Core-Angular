namespace TSIC.Application.Services.Shared.Insurance;

/// <summary>
/// Pure business logic for calculating insurable amounts for Vertical Insure.
/// Converts decimal fee amounts to integer cent values for insurance calculation.
/// </summary>
public static class InsurableAmountCalculator
{
    /// <summary>
    /// Converts a fee amount to insurable amount in cents.
    /// </summary>
    /// <param name="amount">The fee amount in dollars.</param>
    /// <returns>The insurable amount in cents (integer).</returns>
    public static int ComputeInsurableAmount(decimal amount)
        => (int)(amount * 100);

    /// <summary>
    /// Calculates insurable amount with fee precedence logic:
    /// 1. Centralized fee (if > 0)
    /// 2. Per-registrant fee (if set and > 0)
    /// 3. Total fee (fallback)
    /// </summary>
    public static int ComputeInsurableAmountFromCentralized(decimal centralizedFee, decimal? perRegistrantFee, decimal feeTotal)
    {
        if (centralizedFee > 0m) return ComputeInsurableAmount(centralizedFee);
        if (perRegistrantFee.HasValue && perRegistrantFee.Value > 0) return ComputeInsurableAmount(perRegistrantFee.Value);
        return ComputeInsurableAmount(feeTotal);
    }
}

