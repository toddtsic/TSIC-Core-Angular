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
    /// Net insurable amount in cents: the full configured registration price adjusted by
    /// the per-registration fee MODIFIERS — minus EVERY stamped discount, plus late fees —
    /// floored at $0. Processing surcharge and donation are deliberately excluded: neither is
    /// part of the forfeitable registration cost. Shared by the player and team offer builds so
    /// both reflect modifiers identically.
    /// </summary>
    /// <param name="baseFullPrice">Full configured price (deposit + balance), phase-independent.</param>
    /// <param name="feeDiscount">
    /// TOTAL stamped discount — pass <c>TotalDiscount()</c> (FeeDiscount + FeeDiscountMp), the same
    /// total <c>FeeMath</c> subtracts from FeeTotal. This drives a real premium the family pays: a
    /// basis that ignores a discount insures more than the registration can forfeit, so the family is
    /// over-charged for cover it can never claim.
    /// </param>
    /// <param name="feeLatefee">Stamped late-fee total.</param>
    public static int ComputeNetInsurableAmount(decimal baseFullPrice, decimal feeDiscount, decimal feeLatefee)
        => ComputeInsurableAmount(Math.Max(0m, baseFullPrice - feeDiscount + feeLatefee));
}

