namespace TSIC.Domain.Constants;

public static class FeeConstants
{
    /// <summary>
    /// Minimum processing fee rate stored as percentage (3.5 = 3.5%).
    /// Jobs can only override upward. NULL column = grandfathered at this rate.
    /// </summary>
    public const decimal MinProcessingFeePercent = 3.5m;

    // Modifier types stored in fees.FeeModifiers.ModifierType
    public const string ModifierEarlyBird = "EarlyBird";
    public const string ModifierLateFee = "LateFee";
    public const string ModifierDiscount = "Discount";
}
