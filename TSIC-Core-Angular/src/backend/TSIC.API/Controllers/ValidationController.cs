using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using TSIC.API.Services.Shared.UsLax;

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

    public ValidationController(ILogger<ValidationController> logger, IUsLaxService usLaxService)
    {
        _logger = logger;
        _usLaxService = usLaxService;
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
    /// Proxy endpoint for USA Lacrosse membership verification using official API.
    /// Returns raw JSON from USALax (or a simplified error JSON) to the client.
    /// Client performs last-name/DOB/expiration checks.
    /// </summary>
    /// <param name="number">Membership number</param>
    /// <param name="lastName">Optional last name (not used server-side)</param>
    /// <param name="dob">Optional DOB (not used server-side)</param>
    /// <param name="validThrough">Optional valid-through date (not used server-side)</param>
    [HttpGet("uslax")]
    public async Task<IActionResult> ValidateUsLax([FromQuery] string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return BadRequest(new { message = "number is required" });
        try
        {
            var content = await _usLaxService.GetMemberRawJsonAsync(number);
            if (string.IsNullOrEmpty(content))
            {
                return Ok(new { membership = (object?)null, message = "Validation service temporarily unavailable" });
            }
            return Content(content, "application/json", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USLax validation failed for number {Number}", number);
            return Ok(new { membership = (object?)null, message = "Validation service temporarily unavailable" });
        }
    }
}

