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

    /// <summary>
    /// Reformat existing bulletin HTML for better UX + styling. Unlike /bulletin
    /// (which takes a prompt and returns a new draft), this takes the current HTML
    /// and returns a restructured version with token markers where appropriate.
    /// </summary>
    [HttpPost("bulletin/format")]
    public async Task<ActionResult<AiFormatResponse>> FormatBulletin(
        [FromBody] AiFormatBulletinRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required" });

        if (string.IsNullOrWhiteSpace(request.Html))
            return BadRequest(new { message = "Html is required" });

        var result = await _aiComposeService.FormatBulletinAsync(jobId.Value, request.Html, ct);
        return Ok(result);
    }
}

public sealed record AiFormatBulletinRequest
{
    public required string Html { get; init; }
}
