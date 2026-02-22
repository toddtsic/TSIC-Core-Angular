using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/email-log")]
[Authorize(Policy = "AdminOnly")]
public class EmailLogController : ControllerBase
{
    private readonly IEmailLogRepository _emailLogRepo;
    private readonly IJobLookupService _jobLookupService;

    public EmailLogController(
        IEmailLogRepository emailLogRepo,
        IJobLookupService jobLookupService)
    {
        _emailLogRepo = emailLogRepo;
        _jobLookupService = jobLookupService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<EmailLogSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<EmailLogSummaryDto>>> GetEmailLogs(
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var logs = await _emailLogRepo.GetByJobIdAsync(jobId.Value, cancellationToken);
        return Ok(logs);
    }

    [HttpGet("{emailId:int}")]
    [ProducesResponseType(typeof(EmailLogDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmailLogDetailDto>> GetEmailDetail(
        int emailId,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var detail = await _emailLogRepo.GetDetailAsync(emailId, jobId.Value, cancellationToken);
        if (detail == null)
        {
            return NotFound();
        }

        return Ok(detail);
    }
}
