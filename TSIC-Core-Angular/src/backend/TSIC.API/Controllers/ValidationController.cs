using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Contracts.Repositories;
using TSIC.Domain.UsLax;

namespace TSIC.API.Controllers;

/// <summary>
/// Remote validation endpoints for form fields
/// Public endpoints for registration form validation
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class ValidationController : ControllerBase
{
    private readonly ILogger<ValidationController> _logger;
    private readonly IUsLaxService _usLaxService;
    private readonly IJobRepository _jobRepository;

    public ValidationController(
        ILogger<ValidationController> logger,
        IUsLaxService usLaxService,
        IJobRepository jobRepository)
    {
        _logger = logger;
        _usLaxService = usLaxService;
        _jobRepository = jobRepository;
    }

    /// <summary>
    /// Validate USA Lacrosse membership ID via their API
    /// </summary>
    /// <param name="sportAssnID">USA Lacrosse membership number</param>
    /// <returns>True if valid, false otherwise</returns>
    /// <remarks>
    /// Placeholder: format checks only. Real proxy endpoint: GET /api/validation/uslax
    /// </remarks>
    [HttpGet("ValidateUSALacrosseID")]
    public async Task<ActionResult<bool>> ValidateUSALacrosseID([FromQuery] string sportAssnID)
    {
        if (string.IsNullOrWhiteSpace(sportAssnID))
        {
            return BadRequest(new { valid = false, message = "USA Lacrosse ID is required" });
        }

        try
        {
            // For now, just validate format (example: must be numeric and certain length)

            _logger.LogInformation("Validating USA Lacrosse ID: {SportAssnID}", sportAssnID);

            // Placeholder validation logic
            var isNumeric = long.TryParse(sportAssnID, out _);
            var hasValidLength = sportAssnID.Length >= 6 && sportAssnID.Length <= 10;

            if (!isNumeric)
            {
                return Ok(new { valid = false, message = "USA Lacrosse ID must be numeric" });
            }

            if (!hasValidLength)
            {
                return Ok(new { valid = false, message = "USA Lacrosse ID must be between 6 and 10 digits" });
            }

            // Placeholder: Accept all valid-format IDs for now
            return Ok(new { valid = true, message = "USA Lacrosse ID format is valid (API validation not yet implemented)" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating USA Lacrosse ID: {SportAssnID}", sportAssnID);
            return StatusCode(500, new { valid = false, message = "Validation service temporarily unavailable" });
        }
    }

    /// <summary>
    /// USA Lacrosse membership check for the registration form. Returns a VERDICT, not the member.
    ///
    /// This is the new app's equivalent of legacy's <c>ValidationRemote.IsUSLaxNumberValid</c>, which
    /// the wizard calls as the user types. It previously returned USA Lacrosse's raw JSON and left
    /// the decision to the browser — so the checks legacy ran server-side (Player involvement,
    /// lastname, DOB) were simply absent, and the expiry comparison the browser did ran against a
    /// stale JsonOptions key instead of the director's cutoff. All of it now happens here, through
    /// the same <see cref="UsLaxEligibilityPolicy"/> the submit gate uses.
    ///
    /// Anonymous by necessity — families register without an account — so it returns no member
    /// detail: the old passthrough leaked a stranger's name, DOB, email and postal code to anyone
    /// who could guess a number.
    /// </summary>
    /// <param name="number">Membership number (6–12 digits).</param>
    /// <param name="jobPath">Job being registered for — supplies the director's valid-through date.</param>
    /// <param name="lastName">Registrant's last name, matched against USA Lacrosse's record.</param>
    /// <param name="dob">Registrant's date of birth, matched against USA Lacrosse's record.</param>
    /// <param name="teamId">Team being registered for; honors its bDoNotValidateUSLaxNumber opt-out.</param>
    [HttpGet("uslax")]
    public async Task<ActionResult<UsLaxValidationResultDto>> ValidateUsLax(
        [FromQuery] string number,
        [FromQuery] string? jobPath,
        [FromQuery] string? lastName,
        [FromQuery] DateTime? dob,
        [FromQuery] Guid? teamId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(number)) return BadRequest(new { message = "number is required" });
        var trimmed = number.Trim();
        if (!Regex.IsMatch(trimmed, @"^\d{6,12}$"))
        {
            return BadRequest(new { message = "Membership number must be 6 to 12 digits" });
        }

        // The test number bypasses before any lookup, so it works with no jobPath — same as legacy,
        // where it was the first statement in the action.
        if (string.Equals(trimmed, UsLaxEligibilityPolicy.TestMembershipNumber, StringComparison.Ordinal))
            return Ok(Verdict(true, UsLaxEligibilityReason.TestNumber, null));

        if (string.IsNullOrWhiteSpace(jobPath))
            return BadRequest(new { message = "jobPath is required" });

        var jobCtx = await _jobRepository.GetUsLaxValidationContextAsync(jobPath, teamId, ct);
        if (jobCtx is null) return NotFound(new { message = $"Job not found: {jobPath}" });

        UsLaxMemberPingResult? member = null;
        try
        {
            member = await _usLaxService.GetMemberAsync(trimmed, ct);
        }
        catch (Exception ex)
        {
            // Swallowed to a transient verdict, never an exception the wizard has to interpret.
            _logger.LogError(ex, "USLax validation call failed for job {JobPath}", jobPath);
        }

        var verdict = UsLaxEligibilityPolicy.Evaluate(new UsLaxEligibilityInput
        {
            MembershipNumber = trimmed,
            ValidThrough = jobCtx.ValidThrough,
            TeamValidationDisabled = jobCtx.TeamValidationDisabled,
            // Null result = transport/parse failure; policy maps status 0 to "try again", not "invalid".
            VendorStatusCode = member?.StatusCode ?? 0,
            VendorMemStatus = member?.Output?.MemStatus,
            VendorExpDate = member?.Output?.ExpDate,
            VendorLastName = member?.Output?.LastName,
            VendorBirthdate = member?.Output?.Birthdate,
            VendorInvolvement = member?.Output?.Involvement,
            RegistrantLastName = lastName,
            RegistrantDob = dob
        });

        if (!verdict.Valid)
        {
            // Reason only — logging the number or the member's details here would put PII in Seq.
            _logger.LogInformation("USLax check rejected for job {JobPath}: {Reason}", jobPath, verdict.Reason);
        }

        return Ok(Verdict(verdict.Valid, verdict.Reason, UsLaxEligibilityPolicy.MessageFor(verdict)));
    }

    private static UsLaxValidationResultDto Verdict(bool valid, UsLaxEligibilityReason reason, string? message) =>
        new() { Valid = valid, Reason = reason.ToString(), Message = message };
}

