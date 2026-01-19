using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.API.Services.Payments;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/team-payment")]
[Authorize]
public class TeamPaymentController : ControllerBase
{
    private const string UserNotAuthenticatedMessage = "User not authenticated";

    private readonly IPaymentService _paymentService;
    private readonly ILogger<TeamPaymentController> _logger;

    public TeamPaymentController(
        IPaymentService paymentService,
        ILogger<TeamPaymentController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Process team registration payment.
    /// Context (jobId, registration) derived from regId token claim.
    /// </summary>
    [HttpPost("process")]
    [ProducesResponseType(typeof(TeamPaymentResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ProcessTeamPayment([FromBody] TeamPaymentRequestDto request)
    {
        // Extract regId from token
        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
        {
            return Unauthorized(new { Message = "Registration ID not found in token. Please select a club first." });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = UserNotAuthenticatedMessage });
        }

        try
        {
            var response = await _paymentService.ProcessTeamPaymentAsync(
                regId,
                userId,
                request.TeamIds,
                request.TotalAmount,
                request.CreditCard);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to process team payment for user {UserId}, regId {RegId}",
                userId, regId);
            return BadRequest(new { Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing team payment for user {UserId}, regId {RegId}",
                userId, regId);
            return StatusCode(500, new { Message = "An error occurred while processing payment" });
        }
    }
}
