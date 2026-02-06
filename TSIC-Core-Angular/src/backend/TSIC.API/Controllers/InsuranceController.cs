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
using TSIC.API.Services.Shared.Jobs;
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
    private readonly IJobLookupService _jobLookupService;

    public InsuranceController(IVerticalInsureService viService, IJobLookupService jobLookupService)
    {
        _viService = viService;
        _jobLookupService = jobLookupService;
    }

    [HttpPost("purchase")]
    [Authorize]
    [ProducesResponseType(typeof(InsurancePurchaseResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> Purchase([FromBody] InsurancePurchaseRequestDto request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.JobPath))
            return BadRequest(new { message = "Invalid insurance purchase request" });

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (familyUserId == null) return Unauthorized();

        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId is null)
            return NotFound(new { message = $"Job not found: {request.JobPath}" });

        var res = await _viService.PurchasePoliciesAsync(jobId.Value, familyUserId, request.RegistrationIds, request.QuoteIds, token: null, card: request.CreditCard, ct: ct);
        if (!res.Success)
        {
            return BadRequest(new InsurancePurchaseResponseDto { Success = false, Error = res.Error, Policies = new() });
        }

        return Ok(new InsurancePurchaseResponseDto { Success = true, Error = null, Policies = res.Policies });
    }

    [HttpGet("team/pre-submit")]
    [Authorize]
    [ProducesResponseType(typeof(PreSubmitTeamInsuranceDto), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetTeamPreSubmit()
    {
        // Extract regId from token
        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
        {
            return Unauthorized(new { Message = "Registration ID not found in token. Please select a club first." });
        }

        // Get jobId from Registration record via regId
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        var result = await _viService.BuildTeamOfferAsync(regId, userId);
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

        // Extract regId from token
        var regIdClaim = User.FindFirst("regId")?.Value;
        if (string.IsNullOrEmpty(regIdClaim) || !Guid.TryParse(regIdClaim, out var regId))
        {
            return Unauthorized(new { Message = "Registration ID not found in token. Please select a club first." });
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        var res = await _viService.PurchaseTeamPoliciesAsync(
            regId,
            userId,
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
