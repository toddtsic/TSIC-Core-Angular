using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
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
using TSIC.API.Services.Shared.Registration;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.UsLax;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/player-registration")]
public class PlayerRegistrationQueriesController : ControllerBase
{
    private readonly IRegistrationQueryService _queryService;

    public PlayerRegistrationQueriesController(IRegistrationQueryService queryService)
    {
        _queryService = queryService;
    }

    /// <summary>
    /// Returns an existing registration snapshot for a family user in the context of a job.
    /// Shape is compatible with the wizard prefill expectations: teams per player and form values per player.
    /// Family user ID is extracted from JWT claims (sub).
    /// </summary>
    [HttpGet("existing")]
    [Authorize]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetExistingRegistration([FromQuery] string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
        {
            return BadRequest(new { message = "jobPath is required" });
        }

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        try
        {
            var result = await _queryService.GetExistingRegistrationAsync(jobPath, familyUserId, familyUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }
    }

    /// <summary>
    /// Returns a flat list of registrations (one row per registration) for a given family within a job.
    /// Useful for payment/checkout flows where each team/camp is a distinct registration.
    /// Family user ID is extracted from JWT claims (sub).
    /// </summary>
    [HttpGet("family-registrations")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<FamilyRegistrationItemDto>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetFamilyRegistrations([FromQuery] string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
            return BadRequest(new { message = "jobPath is required" });

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        try
        {
            var result = await _queryService.GetFamilyRegistrationsAsync(jobPath, familyUserId, familyUserId);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }
    }

}
