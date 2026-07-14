using TSIC.Contracts.Payments;

namespace TSIC.Contracts.Extensions;

/// <summary>
/// The one discount expression for every read-model that implements <see cref="IFeeDiscountBuckets"/>
/// — the projection-DTO twin of the entity <c>TotalDiscount()</c> overloads in
/// <see cref="RegistrationFeeExtensions"/> and <see cref="TeamFeeExtensions"/>.
/// </summary>
public static class FeeDiscountBucketExtensions
{
    /// <summary>
    /// The TOTAL discount — every bucket <see cref="FeeMath"/> subtracts from FeeTotal.
    /// <b>Pass this to any helper that takes a <c>discount</c> scalar</b> (owed, principal-remaining,
    /// processing target, effective late fee, discount-code basis, insurable amount) — never
    /// <c>FeeDiscount</c> alone. Netting a smaller discount than FeeMath subtracted re-derives a
    /// principal that disagrees with the stored FeeTotal.
    /// </summary>
    public static decimal TotalDiscount(this IFeeDiscountBuckets fees)
        => fees.FeeDiscount + fees.FeeDiscountMp;
}
