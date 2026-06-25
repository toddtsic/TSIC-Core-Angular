using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// USA Lacrosse batch membership reconciliation. Admin-only: pings USALax MemberPing
/// for every active Lacrosse Player registration with a SportAssnId on file, and
/// writes any returned exp_date back to Registrations.SportAssnIdexpDate.
///
/// Single-number validation (used during registration flow) lives in ValidationController.
/// </summary>
[ApiController]
[Route("api/uslax-membership")]
[Authorize(Policy = "AdminOnly")]
public class UsLaxMembershipController : ControllerBase
{
    private readonly IUsLaxMembershipService _service;
    private readonly IJobLookupService _jobLookupService;
    private readonly IEmailBatchJobRegistry _batchJobs;

    public UsLaxMembershipController(
        IUsLaxMembershipService service,
        IJobLookupService jobLookupService,
        IEmailBatchJobRegistry batchJobs)
    {
        _service = service;
        _jobLookupService = jobLookupService;
        _batchJobs = batchJobs;
    }

    [HttpGet("candidates")]
    public async Task<ActionResult<IReadOnlyList<UsLaxReconciliationCandidateDto>>> GetCandidates(
        [FromQuery] UsLaxMembershipRole role = UsLaxMembershipRole.Player,
        CancellationToken ct = default)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Registration context required" });

        var candidates = await _service.GetCandidatesAsync(jobId.Value, role, ct);
        return Ok(candidates);
    }

    [HttpPost("reconcile")]
    public async Task<ActionResult<UsLaxReconciliationResponse>> Reconcile(
        [FromBody] UsLaxReconciliationRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Registration context required" });

        var response = await _service.ReconcileAsync(jobId.Value, request ?? new UsLaxReconciliationRequest(), ct);
        return Ok(response);
    }

    [HttpPost("email")]
    public async Task<ActionResult<UsLaxEmailStartResponse>> SendEmail(
        [FromBody] UsLaxEmailRequest request,
        CancellationToken ct)
    {
        if (request is null || request.Recipients is null || request.Recipients.Count == 0)
            return BadRequest(new { message = "At least one recipient is required" });
        if (string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest(new { message = "Subject is required" });
        if (string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { message = "Body is required" });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Registration context required" });

        var senderUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var start = await _service.StartEmailAsync(jobId.Value, senderUserId, request, ct);
        return Ok(start);
    }

    /// <summary>Progress / final summary for a background USLax email batch (404 if unknown/expired).</summary>
    [HttpGet("email/{batchJobId:guid}/status")]
    public ActionResult<EmailBatchJobStatus> GetEmailStatus(Guid batchJobId)
    {
        var status = _batchJobs.Get(batchJobId);
        return status is null ? NotFound() : Ok(status);
    }
}
