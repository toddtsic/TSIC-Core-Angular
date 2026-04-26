using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Payments;

public interface IPaymentService
{
    /// <summary>
    /// Process player registration payment with jobId and familyUserId extracted from JWT claims.
    /// </summary>
    Task<PaymentResponseDto> ProcessPaymentAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId);

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
}
