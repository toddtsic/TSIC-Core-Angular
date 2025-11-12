using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.DTOs;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration")]
public class RegistrationPaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public RegistrationPaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpPost("submit-payment")]
    [Authorize]
    [ProducesResponseType(typeof(PaymentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SubmitPayment([FromBody] PaymentRequestDto request)
    {
        if (request == null || request.CreditCard == null)
        {
            return BadRequest(new { message = "Invalid payment request" });
        }

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, request.FamilyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var result = await _paymentService.ProcessPaymentAsync(request, callerId);
        return result.Success
            ? Ok(result)
            : BadRequest(new { message = result.Message });
    }
}
