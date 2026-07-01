namespace TSIC.Contracts.Services;

/// <summary>
/// Per-job payment-capability facts that depend on the job's merchant (ADN) account
/// rather than on registration state. The single source of truth for "which card types /
/// payment methods may this job's merchant actually process."
///
/// Today this answers AMEX eligibility. AMEX is an allow-listed exception: only customers
/// whose Authorize.Net merchant account accepts American Express appear in the
/// <c>PaymentMethods_NonMCVisa_ClientIds:Amex</c> config array. Fail-closed — an unknown job,
/// a job with no customer, or a customer not on the list resolves to <c>false</c> so we never
/// offer a card type the merchant can't settle.
///
/// Every payment surface that renders the credit-card form (player, adult/coach, team, store
/// checkout, VI-update) resolves this the same way, so the AMEX option can never appear on one
/// flow and be absent on another for the same merchant.
/// </summary>
public interface IJobPaymentFeaturesService
{
    /// <summary>
    /// True iff the job's customer is on the AMEX allow-list. Fail-closed to false for a
    /// null/unknown job, a job with no customer, or a customer absent from the list.
    /// </summary>
    Task<bool> UsesAmexAsync(Guid? jobId, CancellationToken ct = default);
}
