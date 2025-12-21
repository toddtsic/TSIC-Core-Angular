namespace TSIC.Application.Services.Shared;

/// <summary>
/// Pure business logic for discount calculations.
/// Handles both percentage-based and fixed-amount discounts with proper rounding and capping.
/// </summary>
public static class DiscountCalculator
{
    /// <summary>
    /// Calculates the discount amount based on the base amount and discount configuration.
    /// Business rules:
    /// - Percentage discounts: multiply by percentage, round to 2 decimals
    /// - Fixed discounts: use discount value directly
    /// - Result cannot exceed base amount (discount is capped)
    /// - Result cannot be negative
    /// </summary>
    /// <param name="baseAmount">The amount to apply the discount to (e.g., registration fee).</param>
    /// <param name="discountValue">The discount value (percentage 0-100 or fixed dollar amount).</param>
    /// <param name="isPercentage">True if discountValue is a percentage, false if it's a fixed amount.</param>
    /// <returns>The calculated discount amount, capped at baseAmount and floored at 0.</returns>
    public static decimal Calculate(decimal baseAmount, decimal discountValue, bool isPercentage)
    {
        if (baseAmount <= 0) return 0m;
        if (discountValue <= 0) return 0m;

        decimal discount;

        if (isPercentage)
        {
            // Percentage discount: convert to decimal (e.g., 10% = 0.10)
            var percentage = discountValue / 100m;
            discount = Math.Round(baseAmount * percentage, 2);
        }
        else
        {
            // Fixed-amount discount
            discount = discountValue;
        }

        // Cap discount at base amount (can't discount more than the total)
        if (discount > baseAmount)
            discount = baseAmount;

        return discount;
    }
}

