namespace TSIC.Contracts.Payments;

/// <summary>
/// Stable PaymentMethodId GUIDs from reference.Accounting_PaymentMethods,
/// grouped into canonical buckets the PaymentState resolver sums on.
///
/// These are the production primary keys of the reference table — the same
/// GUIDs every write path hardcodes when stamping accounting rows. Comparing
/// on the GUID instead of the freeform PaymentMethod text avoids drift across
/// variants like "Credit Card Payment" vs "Credit Card Payment PIF" or
/// "Correction" vs "Online Correction By Client".
///
/// Excluded by design:
///   • Credit Card Void, Failed Credit Card Payment, Failed Credit Card Credit,
///     Failed E-Check Payment — no money moved.
///   • BALANCE DUE — opening balance entry, not a payment.
///   • Scholarship — semantics unclear (does it count as principal-credit?);
///     leave out until confirmed.
///   • Credit Card Credit (refund) — handled by refund-specific entity column
///     updates today; not yet routed through PaymentState to avoid double-counting.
/// </summary>
public static class PaymentMethodIds
{
    // Credit card charges through ADN — proc fee applies, Payamt is gross.
    public static readonly IReadOnlySet<Guid> CcPaid = new HashSet<Guid>
    {
        Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"), // Credit Card Payment
        Guid.Parse("5C46057C-69DE-4A22-B20F-D2BBDFE3A43A"), // Credit Card Payment PIF
        Guid.Parse("0CF0E4C2-5853-4A45-A7A5-A0D632BE8870"), // Automated Recurrent Billing
    };

    // ACH / eCheck — Payamt is principal; eCheck-specific proc rate applied at swipe.
    public static readonly IReadOnlySet<Guid> Echeck = new HashSet<Guid>
    {
        Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D"), // E-Check Payment
    };

    // Paper checks — Payamt is principal, no proc collected.
    public static readonly IReadOnlySet<Guid> Check = new HashSet<Guid>
    {
        Guid.Parse("32ECA575-A268-E111-9D56-F04DA202060D"), // Check Payment By Client
        Guid.Parse("37ECA575-A268-E111-9D56-F04DA202060D"), // Check Payment By TSIC
    };

    // Cash — Payamt is principal, no proc collected.
    public static readonly IReadOnlySet<Guid> Cash = new HashSet<Guid>
    {
        Guid.Parse("2DECA575-A268-E111-9D56-F04DA202060D"), // Cash By Client
    };

    // Admin-issued corrections — Payamt is principal, no proc collected.
    public static readonly IReadOnlySet<Guid> Correction = new HashSet<Guid>
    {
        Guid.Parse("33ECA575-A268-E111-9D56-F04DA202060D"), // Online Correction By Client
        Guid.Parse("34ECA575-A268-E111-9D56-F04DA202060D"), // Online Correction By TSIC
    };
}
