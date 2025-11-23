using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Dtos;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Handles independent VerticalInsure / RegSaver insurance policy purchases. Registration fee payment
/// is not coupled to this flow; we simply persist returned (or stub-generated) policy numbers.
/// </summary>
[ApiController]
[Route("api/insurance")]
public class InsuranceController : ControllerBase
{
    private readonly IVerticalInsureService _viService;

    public InsuranceController(IVerticalInsureService viService)
    {
        _viService = viService;
    }

    [HttpPost("purchase")]
    [Authorize]
    [ProducesResponseType(typeof(InsurancePurchaseResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Purchase([FromBody] InsurancePurchaseRequestDto request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { message = "Invalid insurance purchase request" });

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, request.FamilyUserId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var res = await _viService.PurchasePoliciesAsync(request.JobId, request.FamilyUserId.ToString(), request.RegistrationIds, request.QuoteIds, token: null, card: request.CreditCard, ct: ct);
        if (!res.Success)
        {
            return BadRequest(new InsurancePurchaseResponseDto { Success = false, Error = res.Error });
        }

        return Ok(new InsurancePurchaseResponseDto { Success = true, Policies = res.Policies });
    }
}