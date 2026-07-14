using TSIC.Contracts.Payments;

namespace TSIC.Contracts.Extensions;

/// <summary>
/// Consistent FeeTotal/OwedTotal recompute for Registrations entities — the Registrations
/// half of <see cref="TeamFeeExtensions"/>. Both delegate to <see cref="FeeMath"/> so the
/// arithmetic has exactly one definition. Use this everywhere instead of inline FeeTotal/
/// OwedTotal math.
/// FeeTotal = FeeBase + FeeProcessing - FeeDiscount - FeeDiscountMp + FeeDonation + FeeLatefee.
/// OwedTotal = FeeTotal - PaidTotal (signed; overpayment stays negative).
/// Both discount buckets are subtracted — see <see cref="FeeMath"/>.
/// </summary>
public static class RegistrationFeeExtensions
{
    /// <summary>
    /// The registration's TOTAL discount — every bucket FeeMath subtracts from FeeTotal:
    /// <c>FeeDiscount</c> (early-bird + discount code) plus <c>FeeDiscountMp</c> (multi-player/sibling).
    /// <b>Use this anywhere a discount is netted off a charge, an owed amount, a processing-fee basis,
    /// or a rating basis</b> — never <c>FeeDiscount</c> alone. Passing a smaller discount than FeeMath
    /// subtracts bills an amount that disagrees with what is owed, and OwedTotal never reconciles.
    /// </summary>
    public static decimal TotalDiscount(this TSIC.Domain.Entities.Registrations registration)
        => registration.FeeDiscount + registration.FeeDiscountMp;

    /// <summary>
    /// Recalculates FeeTotal and OwedTotal from the current fee component fields.
    /// Call after any change to FeeBase, FeeProcessing, FeeDiscount, FeeDiscountMp, FeeDonation,
    /// FeeLatefee, or PaidTotal. Registrations fee columns are non-nullable.
    /// </summary>
    public static void RecalcTotals(this TSIC.Domain.Entities.Registrations registration)
    {
        registration.FeeTotal = FeeMath.ComputeFeeTotal(
            registration.FeeBase,
            registration.FeeProcessing,
            registration.FeeDiscount,
            registration.FeeDiscountMp,
            registration.FeeDonation,
            registration.FeeLatefee);
        registration.OwedTotal = FeeMath.ComputeOwed(registration.FeeTotal, registration.PaidTotal);
    }
}
