using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
using TSIC.API.Services.Shared.UsLax;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")]
public class PlayerRegistrationStatusController : ControllerBase
{
    private readonly IJobLookupService _jobLookupService;

    public PlayerRegistrationStatusController(IJobLookupService jobLookupService)
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
