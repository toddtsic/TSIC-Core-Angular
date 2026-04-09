using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.AiCompose;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Email;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/ai-compose")]
[Authorize(Policy = "AdminOnly")]
public class AiComposeController : ControllerBase
{
    private readonly IAiComposeService _aiComposeService;
    private readonly IJobLookupService _jobLookupService;

    public AiComposeController(
        IAiComposeService aiComposeService,
        IJobLookupService jobLookupService)
    {
        _aiComposeService = aiComposeService;
        _jobLookupService = jobLookupService;
    }

    [HttpPost("email")]
    public async Task<ActionResult<AiComposeResponse>> ComposeEmail(
        [FromBody] AiComposeRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required" });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Prompt is required" });

        var result = await _aiComposeService.ComposeEmailAsync(jobId.Value, request.Prompt, ct);
        return Ok(result);
    }

    [HttpPost("bulletin")]
    public async Task<ActionResult<AiComposeResponse>> ComposeBulletin(
        [FromBody] AiComposeRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required" });

        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { message = "Prompt is required" });

        var result = await _aiComposeService.ComposeBulletinAsync(jobId.Value, request.Prompt, ct);
        return Ok(result);
    }
}
