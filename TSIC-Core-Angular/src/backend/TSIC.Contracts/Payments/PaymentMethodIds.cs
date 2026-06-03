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
///   • Credit Card Void, Failed Credit Card Payment, Failed Credit Card Credit
///     — no money moved (the charge never cleared).
///   • BALANCE DUE — opening balance entry, not a payment.
///
/// NOTE: Failed E-Check Payment IS bucketed (in Echeck), unlike the failed-CC
/// methods. An eCheck settles provisionally and can be RETURNED (NSF) days
/// later — by then the money was already credited, so its negative reversal
/// row must net the bounced payment back out. See the Echeck bucket comment.
/// </summary>
public static class PaymentMethodIds
{
    // Credit-card flow through ADN — Payamt is gross (principal + proc).
    // Refund rows (Credit Card Credit) write Payamt as a NEGATIVE value, so
    // they net into the same sum as a reduction in CC paid. The reverse-out
    // then reduces principal paid by the refund's principal-equivalent;
    // entity columns are also adjusted at refund time, so both sources of
    // truth stay aligned (TeamSearchService.ProcessRefundAsync L337/L366).
    public static readonly IReadOnlySet<Guid> CcPaid = new HashSet<Guid>
    {
        Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"), // Credit Card Payment
        Guid.Parse("5C46057C-69DE-4A22-B20F-D2BBDFE3A43A"), // Credit Card Payment PIF
        Guid.Parse("0CF0E4C2-5853-4A45-A7A5-A0D632BE8870"), // Automated Recurrent Billing
        Guid.Parse("31ECA575-A268-E111-9D56-F04DA202060D"), // Credit Card Credit (refund — negative Payamt)
    };

    // ACH / eCheck — Payamt is principal; eCheck-specific proc rate applied at swipe.
    // NSF / return reversals (Failed E-Check Payment) post LATER as a NEGATIVE
    // Payamt row, so they net into this same sum as a clawback of the bounced
    // payment — mirroring how Credit Card Credit nets in CcPaid. (An eCheck
    // settles provisionally then can be returned; the failed-CC methods, by
    // contrast, mean the charge never cleared, so they move no money and stay out.)
    public static readonly IReadOnlySet<Guid> Echeck = new HashSet<Guid>
    {
        Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D"), // E-Check Payment
        Guid.Parse("2FECA575-A268-E111-9D56-F04DA202060D"), // Failed E-Check Payment (NSF return — negative Payamt, nets out the bounced payment)
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

    // Admin-issued principal credits — Payamt is principal, no proc collected.
    // Scholarship is bucketed here because the new system implements
    // scholarship as a Correction-method row with Comment="Scholarship"
    // (PlayerCheckTests.cs:217); the Scholarship GUID is defensive against
    // any legacy-written rows.
    public static readonly IReadOnlySet<Guid> Correction = new HashSet<Guid>
    {
        Guid.Parse("33ECA575-A268-E111-9D56-F04DA202060D"), // Online Correction By Client
        Guid.Parse("34ECA575-A268-E111-9D56-F04DA202060D"), // Online Correction By TSIC
        Guid.Parse("2CECA575-A268-E111-9D56-F04DA202060D"), // Scholarship (legacy)
    };
}
