using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Arb;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/arb-defensive")]
public class ArbDefensiveController : ControllerBase
{
    private readonly IArbDefensiveService _service;
    private readonly IJobLookupService _jobLookupService;

    public ArbDefensiveController(IArbDefensiveService service, IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Returns registrations flagged by ARB problem type.
    /// </summary>
    [HttpGet("flagged")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<List<ArbFlaggedRegistrantDto>>> GetFlagged(
        [FromQuery] ArbFlagType type,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return Unauthorized();

        var result = await _service.GetFlaggedSubscriptionsAsync(jobId.Value, type, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns the list of available substitution variables for email templates.
    /// </summary>
    [HttpGet("substitution-variables")]
    [Authorize(Policy = "AdminOnly")]
    public ActionResult<List<ArbSubstitutionVariableDto>> GetSubstitutionVariables()
    {
        var variables = new List<ArbSubstitutionVariableDto>
        {
            new() { Token = "!PLAYER", Label = "Player's last name, first name" },
            new() { Token = "!SUBSCRIPTIONID", Label = "Subscription ID" },
            new() { Token = "!SUBSCRIPTIONSTATUS", Label = "Subscription status" },
            new() { Token = "!FEETOTAL", Label = "Total fees for player" },
            new() { Token = "!PAIDTOTAL", Label = "Total payments for player" },
            new() { Token = "!OWEDNOW", Label = "Amount owed as of today" },
            new() { Token = "!OWEDTOTAL", Label = "Total amount owed" },
            new() { Token = "!FAMILYUSERNAME", Label = "Player's family username" },
            new() { Token = "!JOBLINK", Label = "Job name as a clickable link" },
            new() { Token = "!JOBNAME", Label = "Job name" }
        };
        return Ok(variables);
    }

    /// <summary>
    /// Sends batch defensive emails to selected registrants.
    /// </summary>
    [HttpPost("send-emails")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ArbEmailResultDto>> SendEmails(
        [FromBody] ArbSendEmailsRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return Unauthorized();

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Override from JWT claims — frontend doesn't need to supply these
        var enriched = request with { JobId = jobId.Value, SenderUserId = userId };
        var result = await _service.SendDefensiveEmailsAsync(enriched, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns subscription info for the self-service CC update form.
    /// </summary>
    [HttpGet("subscription-info/{registrationId:guid}")]
    [Authorize]
    public async Task<ActionResult<ArbSubscriptionInfoDto>> GetSubscriptionInfo(
        Guid registrationId,
        CancellationToken ct)
    {
        var result = await _service.GetSubscriptionInfoAsync(registrationId, ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Self-service: update credit card on subscription + pay any balance.
    /// </summary>
    [HttpPost("update-cc")]
    [Authorize]
    public async Task<ActionResult<ArbUpdateCcResultDto>> UpdateCreditCard(
        [FromBody] ArbUpdateCcRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _service.UpdateSubscriptionCreditCardAsync(request, userId, ct);
        return Ok(result);
    }
}
