using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Payments;

public interface IPaymentService
{
    Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId);

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
