using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.DTOs;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegistrationController : ControllerBase
{
    private readonly ILogger<RegistrationController> _logger;
    private readonly IJobLookupService _jobLookupService;

    public RegistrationController(
        ILogger<RegistrationController> logger,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _jobLookupService = jobLookupService;
    }

    [AllowAnonymous]
    [HttpPost("check-status")]
    public async Task<ActionResult<IEnumerable<RegistrationStatusResponse>>> CheckJobRegistrationStatus([FromBody] RegistrationStatusRequest request)
    {
        _logger.LogInformation("Checking registration status for job: {JobPath}, types: {Types}",
            request.JobPath, string.Join(", ", request.RegistrationTypes));

        // Lookup job by path
        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);

        if (jobId == null)
        {
            return NotFound(new { message = $"Job not found: {request.JobPath}" });
        }

        var responses = new List<RegistrationStatusResponse>();

        foreach (var regType in request.RegistrationTypes)
        {
            RegistrationStatusResponse response;

            if (regType.Equals("Player", StringComparison.OrdinalIgnoreCase))
            {
                var isActive = await _jobLookupService.IsPlayerRegistrationActiveAsync(jobId.Value);

                response = new RegistrationStatusResponse
                {
                    RegistrationType = regType,
                    IsAvailable = isActive,
                    Message = isActive ? null : "Player registration is not available at this time.",
                    RegistrationUrl = isActive ? $"/{request.JobPath}/register/player" : null
                };
            }
            else
            {
                // TODO: Phase 1 - Add support for Team, ClubRep, Volunteer, Recruiter registration types
                response = new RegistrationStatusResponse
                {
                    RegistrationType = regType,
                    IsAvailable = false,
                    Message = $"{regType} registration is not available at this time.",
                    RegistrationUrl = null
                };
            }

            responses.Add(response);
        }

        return Ok(responses);
    }
}
