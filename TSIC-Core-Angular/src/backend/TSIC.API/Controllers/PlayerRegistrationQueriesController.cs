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
using TSIC.API.Services.External;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Validation;

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
    /// </summary>
    [HttpGet("existing")]
    [Authorize]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetExistingRegistration([FromQuery] string jobPath, [FromQuery] string familyUserId)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(familyUserId))
        {
            return BadRequest(new { message = "jobPath and familyUserId are required" });
        }

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, familyUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        try
        {
            var result = await _queryService.GetExistingRegistrationAsync(jobPath, familyUserId, callerId);
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
    /// </summary>
    [HttpGet("family-registrations")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<FamilyRegistrationItemDto>), 200)]
    public async Task<IActionResult> GetFamilyRegistrations([FromQuery] string jobPath, [FromQuery] string familyUserId)
    {
        var err = ValidateFamilyRequest(jobPath, familyUserId);
        if (err is IActionResult early) return early;
        try
        {
            var result = await _queryService.GetFamilyRegistrationsAsync(jobPath, familyUserId, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Job not found: {jobPath}" });
        }
    }

    private IActionResult? ValidateFamilyRequest(string jobPath, string familyUserId)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(familyUserId))
            return BadRequest(new { message = "jobPath and familyUserId are required" });
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, familyUserId, StringComparison.OrdinalIgnoreCase)) return Forbid();
        return null;
    }

}
