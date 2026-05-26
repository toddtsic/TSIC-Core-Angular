using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Payments;

public interface IPaymentService
{
    /// <summary>
    /// Process player registration payment with jobId and familyUserId extracted from JWT claims.
    /// </summary>
    Task<PaymentResponseDto> ProcessPaymentAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId);

    /// <summary>
    /// Canonical single per-player CC charge engine. One ADN_Charge for the whole list
    /// (parent self-pay sends N regs in one transaction; admin admin-charge passes a
    /// single-element list). Every consumer goes through this method so display, the
    /// charge engine, and admin quoting cannot drift on the amount.
    ///
    /// Audit trail: a placeholder RA row is inserted (Payamt=0, Active=true) BEFORE the
    /// gateway call, so failed charges leave a row with Active=false + Comment="FAILED: …".
    /// On success the same row is updated with Payamt, AdnTransactionId, AdnInvoiceNo,
    /// AdnCc4, AdnCcexpDate, Paymeth and the registration's PaidTotal/OwedTotal advance.
    ///
    /// Each amount is validated against <c>PaymentState.ResolveOwed.Cc</c> for the
    /// registration; the tripwire prevents a stale UI total from charging more than the
    /// resolver currently shows. Caller-side concerns (RegSaver policy stamping, email)
    /// stay outside this method.
    /// </summary>
    Task<RegistrationCcChargeResult> ChargeRegistrationsCcAsync(
        Guid jobId,
        IReadOnlyList<RegistrationChargeItem> items,
        CreditCardInfo creditCard,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// eCheck (ACH) counterpart to <see cref="ProcessPaymentAsync"/>. Charges via
    /// Authorize.Net bank-account debit, writes one RegistrationAccounting row per
    /// registration share, and inserts a matching Settlement row (status Pending) for
    /// each so the daily sweep can detect both clearance and NSF returns.
    /// </summary>
    Task<PaymentResponseDto> ProcessEcheckPaymentAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId);

    /// <summary>
    /// Process team registration payment.
    /// Derives jobId and club info from the registration identified by regId.
    /// </summary>
    Task<TeamPaymentResponseDto> ProcessTeamPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        decimal totalAmount,
        CreditCardInfo creditCard);

    /// <summary>
    /// eCheck (ACH) counterpart to <see cref="ProcessTeamPaymentAsync"/>. Charges each
    /// team as a separate Authorize.Net debit (per-team invoice for refundability),
    /// writes one RegistrationAccounting + Settlement (status Pending) per successful
    /// debit. Job-level BEnableEcheck must be on; the per-team processing-fee credit
    /// (CC_rate − EC_rate) is applied before the gateway call.
    /// </summary>
    Task<TeamPaymentResponseDto> ProcessTeamEcheckPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        decimal totalAmount,
        BankAccountInfo bankAccount);

    /// <summary>
    /// ARB-Trial team registration payment. Creates one ADN ARB subscription per team:
    /// deposit billed tomorrow (trial occurrence), balance billed on the job's configured
    /// AdnStartDateAfterTrial (post-trial occurrence). Capture-what-you-can: stops at
    /// first per-team failure, prior successes persist (ARB subs stay live).
    ///
    /// Fallback: when today is on/after AdnStartDateAfterTrial, a single full-amount
    /// CC/eCheck charge replaces the ARB flow (no sub created). Caller passes either
    /// CreditCard or BankAccount, never both.
    /// </summary>
    Task<TeamArbTrialPaymentResponseDto> ProcessTeamArbTrialPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        CreditCardInfo? creditCard,
        BankAccountInfo? bankAccount);
}
