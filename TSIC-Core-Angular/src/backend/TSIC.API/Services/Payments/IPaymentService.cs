using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Payments;

public interface IPaymentService
{
    /// <summary>
    /// Process player registration payment with jobId and familyUserId extracted from JWT claims.
    /// </summary>
    Task<PaymentResponseDto> ProcessPaymentAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId);

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
