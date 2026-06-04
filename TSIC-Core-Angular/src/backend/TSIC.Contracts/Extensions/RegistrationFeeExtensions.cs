using TSIC.Contracts.Payments;

namespace TSIC.Contracts.Extensions;

/// <summary>
/// Consistent FeeTotal/OwedTotal recompute for Registrations entities — the Registrations
/// half of <see cref="TeamFeeExtensions"/>. Both delegate to <see cref="FeeMath"/> so the
/// arithmetic has exactly one definition. Use this everywhere instead of inline FeeTotal/
/// OwedTotal math.
/// FeeTotal = FeeBase + FeeProcessing - FeeDiscount + FeeDonation + FeeLatefee.
/// OwedTotal = FeeTotal - PaidTotal (signed; overpayment stays negative).
/// (FeeDiscountMp is a retired stub excluded from the formula — see FeeMath.)
/// </summary>
public static class RegistrationFeeExtensions
{
    /// <summary>
    /// Recalculates FeeTotal and OwedTotal from the current fee component fields.
    /// Call after any change to FeeBase, FeeProcessing, FeeDiscount, FeeDonation,
    /// FeeLatefee, or PaidTotal. Registrations fee columns are non-nullable.
    /// </summary>
    public static void RecalcTotals(this TSIC.Domain.Entities.Registrations registration)
    {
        registration.FeeTotal = FeeMath.ComputeFeeTotal(
            registration.FeeBase,
            registration.FeeProcessing,
            registration.FeeDiscount,
            registration.FeeDonation,
            registration.FeeLatefee);
        registration.OwedTotal = FeeMath.ComputeOwed(registration.FeeTotal, registration.PaidTotal);
    }
}
