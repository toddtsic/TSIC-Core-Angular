using TSIC.API.Dtos;

namespace TSIC.API.Services;

public interface IPaymentService
{
    Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId);
}
