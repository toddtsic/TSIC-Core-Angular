using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "SuperUserOnly")]
public class AdministratorsController : ControllerBase
{
    private readonly ILogger<AdministratorsController> _logger;
    private readonly IAdministratorService _adminService;
    private readonly IJobLookupService _jobLookupService;

    public AdministratorsController(
        ILogger<AdministratorsController> logger,
        IAdministratorService adminService,
        IJobLookupService jobLookupService)
    {
        _logger = logger;
        _adminService = adminService;
        _jobLookupService = jobLookupService;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdministratorDto>>> GetAdministrators(
        CancellationToken cancellationToken)
    {
        // Extract jobId from regId claim (most secure approach)
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var administrators = await _adminService.GetAdministratorsAsync(jobId.Value, cancellationToken);
        return Ok(administrators);
    }

    [HttpPost]
    public async Task<ActionResult<AdministratorDto>> AddAdministrator(
        [FromBody] AddAdministratorRequest request,
        CancellationToken cancellationToken)
    {
        // Extract jobId from regId claim (most secure approach)
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var result = await _adminService.AddAdministratorAsync(jobId.Value, request, userId, cancellationToken);
            return CreatedAtAction(nameof(GetAdministrators), null, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{registrationId:guid}")]
    public async Task<ActionResult<AdministratorDto>> UpdateAdministrator(
        Guid registrationId,
        [FromBody] UpdateAdministratorRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var result = await _adminService.UpdateAdministratorAsync(registrationId, request, userId, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{registrationId:guid}")]
    public async Task<ActionResult> DeleteAdministrator(
        Guid registrationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _adminService.DeleteAdministratorAsync(registrationId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("batch-status")]
    public async Task<ActionResult<int>> BatchUpdateStatus(
        [FromBody] BatchUpdateStatusRequest request,
        CancellationToken cancellationToken)
    {
        // Extract jobId from regId claim (most secure approach)
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var count = await _adminService.BatchUpdateStatusAsync(jobId.Value, request.IsActive, cancellationToken);
        return Ok(new { updated = count });
    }

    [HttpPut("{registrationId:guid}/primary-contact")]
    public async Task<ActionResult<List<AdministratorDto>>> SetPrimaryContact(
        Guid registrationId,
        CancellationToken cancellationToken)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        try
        {
            var result = await _adminService.SetPrimaryContactAsync(jobId.Value, registrationId, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("users/search")]
    public async Task<ActionResult<List<UserSearchResultDto>>> SearchUsers(
        [FromQuery] string q,
        CancellationToken cancellationToken)
    {
        // Extract jobId from regId claim (most secure approach)
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
        {
            return BadRequest(new { message = "Registration context required" });
        }

        var results = await _adminService.SearchUsersAsync(q, cancellationToken);
        return Ok(results);
    }
}
