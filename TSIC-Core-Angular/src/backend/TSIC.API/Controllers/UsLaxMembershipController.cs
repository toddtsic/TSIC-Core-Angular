using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using System.Text;
using System.Text.RegularExpressions;
using TSIC.API.Services.Shared.UsLax;
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
    private readonly IHostEnvironment _env;
    private readonly IUsLaxService _usLax;

    public UsLaxMembershipController(
        IUsLaxMembershipService service,
        IJobLookupService jobLookupService,
        IEmailBatchJobRegistry batchJobs,
        IHostEnvironment env,
        IUsLaxService usLax)
    {
        _service = service;
        _jobLookupService = jobLookupService;
        _batchJobs = batchJobs;
        _env = env;
        _usLax = usLax;
    }

    /// <summary>
    /// Raw MemberPing lookup for one number — the Tools → USLax Test diagnostic panel.
    ///
    /// This is the member record USA Lacrosse holds (name, DOB, email, postal code), so it lives
    /// here behind AdminOnly rather than on the anonymous registration endpoint, which used to
    /// return it to any caller who could guess a membership number.
    /// </summary>
    [HttpGet("member/{number}")]
    public async Task<IActionResult> GetMember(string number, CancellationToken ct = default)
    {
        var trimmed = (number ?? string.Empty).Trim();
        if (!Regex.IsMatch(trimmed, @"^\d{6,12}$"))
            return BadRequest(new { message = "Membership number must be 6 to 12 digits" });

        var content = await _usLax.GetMemberRawJsonAsync(trimmed, ct);
        if (string.IsNullOrEmpty(content))
            return Ok(new { status_code = 0, output = (object?)null, message = "USA Lacrosse is unreachable right now." });

        return Content(content, "application/json", Encoding.UTF8);
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

    /// <summary>
    /// Sandbox-only: renders the composed USLax email for one recipient snapshot and delivers it
    /// FOR REAL to a single test inbox. Rejected in Production.
    /// </summary>
    [HttpPost("email/test-send")]
    public async Task<ActionResult<EmailTestSendResponse>> SendTestEmail(
        [FromBody] UsLaxTestSendRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            return BadRequest(new { message = "Subject and body are required" });

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null) return BadRequest(new { message = "Registration context required" });

        if (_env.IsLiveProduction())
            return BadRequest(new { message = "Test sends are not permitted in Production." });

        var result = await _service.SendTestEmailAsync(jobId.Value, request, ct);
        return Ok(result);
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
