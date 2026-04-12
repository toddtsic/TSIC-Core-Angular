using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.AdultRegistration;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/adult-registration")]
public class AdultRegistrationController : ControllerBase
{
    private readonly IAdultRegistrationService _service;
    private readonly IJobLookupService _jobLookupService;

    public AdultRegistrationController(
        IAdultRegistrationService service,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Get job info and available adult roles (anonymous).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{jobPath}/job-info")]
    public async Task<IActionResult> GetJobInfo(string jobPath, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetJobInfoByPathAsync(jobPath, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get dynamic form schema and waivers for a selected role (anonymous).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{jobPath}/form-schema/{roleType}")]
    public async Task<IActionResult> GetFormSchema(string jobPath, AdultRoleType roleType, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetFormSchemaForRoleAsync(jobPath, roleType, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Register a new adult user (creates account + registration, anonymous).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{jobPath}/register")]
    public async Task<IActionResult> RegisterNewUser(string jobPath, [FromBody] AdultRegistrationRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.RegisterNewUserAsync(jobPath, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Register an existing (logged-in) user as an adult (authenticated).
    /// </summary>
    [Authorize]
    [HttpPost("register-existing")]
    public async Task<IActionResult> RegisterExistingUser([FromBody] AdultRegistrationExistingRequest request, CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "User identity not found." });

            var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
            if (jobId is null)
                return BadRequest(new { message = "Unable to resolve job from current session." });

            var result = await _service.RegisterExistingUserAsync(jobId.Value, userId, request, userId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get available teams for Coach registration (legacy ListAvailableTeams).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{jobPath}/available-teams")]
    public async Task<IActionResult> GetAvailableTeams(string jobPath, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetAvailableTeamsAsync(jobPath, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Returns the authenticated user's existing active registrations for
    /// (jobPath, roleKey). Used to prefill the Profile step on return visits.
    /// </summary>
    [Authorize]
    [HttpGet("{jobPath}/my-registration/{roleKey}")]
    public async Task<IActionResult> GetMyExistingRegistration(string jobPath, string roleKey, CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "User identity not found." });

            var result = await _service.GetMyExistingRegistrationAsync(jobPath, roleKey, userId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Unified role configuration endpoint — returns display metadata, team-selection
    /// requirement, profile fields, and waivers for a given (jobPath, roleKey).
    /// Security invariant (tournament coach + BAllowRosterViewAdult=false) is enforced here.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{jobPath}/role-config/{roleKey}")]
    public async Task<IActionResult> GetRoleConfig(string jobPath, string roleKey, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetRoleConfigAsync(jobPath, roleKey, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// PreSubmit: validates form fields, resolves fees, optionally creates registration (login-mode).
    /// Accepts both anonymous (create-mode) and authenticated (login-mode) callers.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("{jobPath}/pre-submit")]
    public async Task<IActionResult> PreSubmit(string jobPath, [FromBody] PreSubmitAdultRegRequestDto request, CancellationToken ct)
    {
        try
        {
            var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
            if (jobId == null)
                return NotFound(new { message = $"Job not found for path '{jobPath}'." });

            // Determine userId: present if authenticated (login-mode), null if anonymous (create-mode)
            string? userId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }

            var result = await _service.PreSubmitAsync(jobId.Value, userId, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Process payment for an adult registration (authenticated, login-mode).
    /// </summary>
    [Authorize]
    [HttpPost("submit-payment")]
    public async Task<IActionResult> SubmitPayment([FromBody] AdultPaymentRequestDto request, CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized(new { message = "User identity not found." });

            var result = await _service.ProcessPaymentAsync(request.RegistrationId, userId, request, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get confirmation content after registration (authenticated).
    /// </summary>
    [Authorize]
    [HttpGet("confirmation/{registrationId:guid}")]
    public async Task<IActionResult> GetConfirmation(Guid registrationId, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetConfirmationAsync(registrationId, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Resend confirmation email (authenticated).
    /// </summary>
    [Authorize]
    [HttpPost("confirmation/{registrationId:guid}/resend")]
    public async Task<IActionResult> ResendConfirmationEmail(Guid registrationId, CancellationToken ct)
    {
        try
        {
            await _service.SendConfirmationEmailAsync(registrationId, ct);
            return Ok(new { message = "Confirmation email sent." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
