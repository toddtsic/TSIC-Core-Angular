using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.EmailTroubleshooter;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Player-facing email deliverability self-service (companion to the admin
/// EmailTroubleshooterController). A logged-in family can, for their own emails in the current job
/// only (mom/dad/each player): check Amazon SES suppression status, self-unsuppress, send a real
/// test message, and review this job's send history.
///
/// Security: the caller never supplies an address to act on. The family login (JWT subject) and
/// job (derived from the immutable regId claim) resolve the sendable set server-side; unsuppress
/// and test-send are refused for any address outside it. Bare [Authorize] — the token is the
/// boundary, matching AccountController/FamilyController "self" endpoints.
/// </summary>
[ApiController]
[Route("api/my-email-deliverability")]
[Authorize]
public class MyEmailDeliverabilityController : ControllerBase
{
    private readonly IMyEmailDeliverabilityService _service;
    private readonly IJobLookupService _jobLookup;

    public MyEmailDeliverabilityController(
        IMyEmailDeliverabilityService service,
        IJobLookupService jobLookup)
    {
        _service = service;
        _jobLookup = jobLookup;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(IReadOnlyList<SuppressionEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SuppressionEntryDto>>> GetStatus(
        CancellationToken cancellationToken)
    {
        var (familyUserId, jobId, fail) = await ResolveContextAsync();
        if (fail is not null) return fail;

        var results = await _service.GetStatusAsync(jobId, familyUserId, cancellationToken);
        return Ok(results);
    }

    [HttpPost("unsuppress")]
    [ProducesResponseType(typeof(SuppressionRemoveResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SuppressionRemoveResultDto>> Unsuppress(
        [FromBody] MyEmailAddressRequest request,
        CancellationToken cancellationToken)
    {
        var (familyUserId, jobId, fail) = await ResolveContextAsync();
        if (fail is not null) return fail;

        var result = await _service.UnsuppressAsync(jobId, familyUserId, request.Email, cancellationToken);
        if (result is null)
        {
            // Address is not one of the caller's own — never touched SES.
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Ok(result);
    }

    [HttpPost("test-send")]
    [ProducesResponseType(typeof(EmailInvestigateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EmailInvestigateResultDto>> TestSend(
        [FromBody] MyEmailAddressRequest request,
        CancellationToken cancellationToken)
    {
        var (familyUserId, jobId, fail) = await ResolveContextAsync();
        if (fail is not null) return fail;

        var result = await _service.TestSendAsync(jobId, familyUserId, request.Email, cancellationToken);
        if (result is null)
        {
            // Address is not one of the caller's own — never sent.
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Ok(result);
    }

    [HttpGet("sent-history")]
    [ProducesResponseType(typeof(IReadOnlyList<PlayerSentEmailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlayerSentEmailDto>>> GetSentHistory(
        CancellationToken cancellationToken)
    {
        var (familyUserId, jobId, fail) = await ResolveContextAsync();
        if (fail is not null) return fail;

        var results = await _service.GetSentHistoryAsync(jobId, familyUserId, cancellationToken);
        return Ok(results);
    }

    [HttpGet("sent-history/{emailId:int}/template")]
    [Produces("text/html")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSentTemplate(
        int emailId,
        CancellationToken cancellationToken)
    {
        var (familyUserId, jobId, fail) = await ResolveContextAsync();
        if (fail is not null) return fail;

        var template = await _service.GetSentTemplateAsync(jobId, familyUserId, emailId, cancellationToken);
        if (template is null)
        {
            // Not a batch dispatched to one of the caller's own addresses in this job.
            return NotFound();
        }

        // Raw HTML body (client renders it, sanitized) — return as content, not a JSON-quoted string.
        return Content(template, "text/html");
    }

    /// <summary>Resolve the family login (JWT subject) and job (from the immutable regId claim).</summary>
    private async Task<(string FamilyUserId, Guid JobId, ActionResult? Fail)> ResolveContextAsync()
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(familyUserId))
        {
            return (string.Empty, Guid.Empty, Unauthorized());
        }

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookup);
        if (jobId is null)
        {
            return (string.Empty, Guid.Empty, BadRequest("No job context on this session."));
        }

        return (familyUserId, jobId.Value, null);
    }
}
