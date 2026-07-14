namespace TSIC.Contracts.Payments;

/// <summary>
/// A read-model that carries a registration's/team's discount into a money calculation.
///
/// <para>
/// <b>Why this interface exists.</b> <see cref="FeeMath"/> subtracts TWO discount buckets from
/// FeeTotal — <c>FeeDiscount</c> and <c>FeeDiscountMp</c>. Every helper that re-derives money from
/// components (<see cref="PaymentState.ResolveOwed"/>, <see cref="PaymentState.PrincipalRemaining"/>,
/// <see cref="PaymentState.FeeProcessingTarget"/>, <see cref="PaymentState.EffectiveLateFee"/>, the
/// discount-code rating basis, the insurable-amount basis) takes a SINGLE <c>discount</c> scalar. If a
/// projection only carries <c>FeeDiscount</c>, the call site physically cannot pass the total, and the
/// recomputed principal silently disagrees with the stored FeeTotal.
/// </para>
///
/// <para>
/// So the rule is structural, not a matter of discipline: <b>a DTO that carries a discount into a
/// calculation implements this interface</b>, which forces it to declare BOTH buckets, and the call
/// site passes <c>TotalDiscount()</c> (the single extension over this interface) rather than reaching
/// for a field. Adding a new money projection with only one bucket now fails to compile against the
/// helpers that need the total.
/// </para>
///
/// The entities are the exception by shape, not by rule: <c>Registrations</c> (non-nullable columns)
/// and <c>Teams</c> (nullable) are scaffolded and cannot be made to implement this, so they carry their
/// own <c>TotalDiscount()</c> overloads in <c>RegistrationFeeExtensions</c> / <c>TeamFeeExtensions</c>.
/// Same name, same meaning, same total.
/// </summary>
public interface IFeeDiscountBuckets
{
    /// <summary>Early-bird (a FeeModifier) plus any redeemed discount code.</summary>
    decimal FeeDiscount { get; }

    /// <summary>
    /// Multi-player / sibling discount. Currently 0.00 on every row — no code writes it — but it is
    /// carried through every calculation so the feature can be built without re-auditing the money paths.
    /// </summary>
    decimal FeeDiscountMp { get; }
}
