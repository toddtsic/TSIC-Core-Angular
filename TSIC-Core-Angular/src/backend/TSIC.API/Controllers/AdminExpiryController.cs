using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services.Admin;
using TSIC.Contracts.Dtos.AdminExpiry;

namespace TSIC.API.Controllers;

/// <summary>
/// SuperUser Admin Expiry tool (migrated from legacy AdminExpiryController):
/// cross-customer discovery of jobs whose admin door (ExpiryAdmin) has closed,
/// and the one-field update to reopen a job. Deliberately jobPath-agnostic —
/// the policy alone gates it, same as JobClone.
/// </summary>
[ApiController]
[Route("api/admin-expiry")]
[Authorize(Policy = "SuperUserOnly")]
public class AdminExpiryController : ControllerBase
{
    private readonly IAdminExpiryService _adminExpiryService;

    public AdminExpiryController(IAdminExpiryService adminExpiryService)
    {
        _adminExpiryService = adminExpiryService;
    }

    /// <summary>
    /// All jobs with ExpiryAdmin in the past, grouped by owning customer
    /// (customers and jobs each ordered by name).
    /// </summary>
    [HttpGet("expired-jobs")]
    public async Task<ActionResult<List<AdminExpiryCustomerDto>>> GetExpiredJobs(CancellationToken ct)
    {
        var result = await _adminExpiryService.GetExpiredJobsAsync(ct);
        return Ok(result);
    }

    /// <summary>Set a job's ExpiryAdmin to reopen (or further close) its admin door.</summary>
    [HttpPut("jobs/{jobId:guid}")]
    public async Task<IActionResult> UpdateExpiry(
        Guid jobId, [FromBody] UpdateAdminExpiryRequest request, CancellationToken ct)
    {
        try
        {
            await _adminExpiryService.UpdateExpiryAsync(jobId, request, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
