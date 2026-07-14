using TSIC.Contracts.Payments;

namespace TSIC.Contracts.Extensions;

/// <summary>
/// Consistent FeeTotal/OwedTotal recompute for team entities — the Teams half of
/// <see cref="RegistrationFeeExtensions"/>. Both delegate to <see cref="FeeMath"/> so the
/// arithmetic has exactly one definition. Use this everywhere instead of inline math.
/// FeeTotal = FeeBase + FeeProcessing - FeeDiscount - FeeDiscountMp + FeeDonation + FeeLatefee.
/// OwedTotal = FeeTotal - PaidTotal (signed; overpayment stays negative).
/// Both discount buckets are subtracted — see <see cref="FeeMath"/>.
/// </summary>
public static class TeamFeeExtensions
{
    /// <summary>
    /// The team's TOTAL discount — every bucket FeeMath subtracts from FeeTotal.
    /// <b>Use this anywhere a discount is netted off a charge, an owed amount, a processing-fee
    /// basis, or a rating basis</b> — never <c>FeeDiscount</c> alone. Passing a smaller discount than
    /// FeeMath subtracts bills an amount that disagrees with what is owed.
    /// </summary>
    public static decimal TotalDiscount(this TSIC.Domain.Entities.Teams team)
        => (team.FeeDiscount ?? 0m) + (team.FeeDiscountMp ?? 0m);

    /// <summary>
    /// Recalculates FeeTotal and OwedTotal from current fee fields.
    /// Call this after any change to FeeBase, FeeProcessing, FeeDiscount, FeeDiscountMp, FeeDonation,
    /// FeeLatefee, or PaidTotal. Teams fee columns are nullable, so callers coalesce here.
    /// </summary>
    public static void RecalcTotals(this TSIC.Domain.Entities.Teams team)
    {
        team.FeeTotal = FeeMath.ComputeFeeTotal(
            team.FeeBase ?? 0m,
            team.FeeProcessing ?? 0m,
            team.FeeDiscount ?? 0m,
            team.FeeDiscountMp ?? 0m,
            team.FeeDonation ?? 0m,
            team.FeeLatefee ?? 0m);
        team.OwedTotal = FeeMath.ComputeOwed(team.FeeTotal ?? 0m, team.PaidTotal ?? 0m);
    }

    /// <summary>
    /// Sets FeeBase + FeeProcessing from calculator output, then recalculates totals.
    /// </summary>
    public static void ApplyCalculatedFees(this TSIC.Domain.Entities.Teams team, decimal feeBase, decimal feeProcessing)
    {
        team.FeeBase = feeBase;
        team.FeeProcessing = feeProcessing;
        team.RecalcTotals();
    }
}
