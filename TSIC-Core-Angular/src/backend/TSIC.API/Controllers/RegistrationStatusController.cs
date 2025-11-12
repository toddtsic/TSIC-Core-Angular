using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.DTOs;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/registration")]
public class RegistrationStatusController : ControllerBase
{
    private readonly IJobLookupService _jobLookupService;

    public RegistrationStatusController(IJobLookupService jobLookupService)
    {
        _jobLookupService = jobLookupService;
    }

    [AllowAnonymous]
    [HttpPost("check-status")]
    public async Task<ActionResult<IEnumerable<RegistrationStatusResponse>>> CheckJobRegistrationStatus([FromBody] RegistrationStatusRequest request)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(request.JobPath);
        if (jobId == null)
        {
            return NotFound(new { message = $"Job not found: {request.JobPath}" });
        }

        var responses = new List<RegistrationStatusResponse>();
        foreach (var regType in request.RegistrationTypes)
        {
            if (regType.Equals("Player", StringComparison.OrdinalIgnoreCase))
            {
                var isActive = await _jobLookupService.IsPlayerRegistrationActiveAsync(jobId.Value);
                responses.Add(new RegistrationStatusResponse
                {
                    RegistrationType = regType,
                    IsAvailable = isActive,
                    Message = isActive ? null : "Player registration is not available at this time.",
                    RegistrationUrl = isActive ? $"/{request.JobPath}/register/player" : null
                });
            }
            else
            {
                responses.Add(new RegistrationStatusResponse
                {
                    RegistrationType = regType,
                    IsAvailable = false,
                    Message = $"{regType} registration is not available at this time.",
                    RegistrationUrl = null
                });
            }
        }

        return Ok(responses);
    }
}
