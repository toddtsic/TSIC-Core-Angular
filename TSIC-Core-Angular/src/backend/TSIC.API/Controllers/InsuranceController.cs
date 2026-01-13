using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Families;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Shared.UsLax;

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

    [HttpGet("team/pre-submit")]
    [Authorize]
    [ProducesResponseType(typeof(PreSubmitTeamInsuranceDto), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetTeamPreSubmit([FromQuery] Guid jobId, [FromQuery] Guid clubRepRegId)
    {
        var result = await _viService.BuildTeamOfferAsync(jobId, clubRepRegId);
        return Ok(result);
    }

    [HttpPost("team/purchase")]
    [Authorize]
    [ProducesResponseType(typeof(TeamInsurancePurchaseResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> PurchaseTeam([FromBody] TeamInsurancePurchaseRequestDto request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { message = "Invalid team insurance purchase request" });

        var res = await _viService.PurchaseTeamPoliciesAsync(
            request.JobId,
            request.ClubRepRegId,
            request.TeamIds,
            request.QuoteIds,
            token: null,
            card: request.CreditCard,
            ct: ct);

        if (!res.Success)
        {
            return BadRequest(new TeamInsurancePurchaseResponseDto { Success = false, Error = res.Error });
        }

        return Ok(new TeamInsurancePurchaseResponseDto { Success = true, Policies = res.Policies });
    }
}
