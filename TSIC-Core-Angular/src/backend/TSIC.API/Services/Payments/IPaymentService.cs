using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Payments;

public interface IPaymentService
{
    Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId);
}
