using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Dtos;
using TSIC.API.Constants;
using System.Transactions;
using System.Globalization;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Text.Json;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FamilyController : ControllerBase
{
    private readonly SqlDbContext _db;
    private readonly IFamilyService _familyService;
    // DateFormat constant moved into FamilyService

    // Generic mapper: include ANY dbColumn-backed metadata field present on Registrations.
    // Avoid special-casing (fees, waivers, sportAssnId, etc.) so client can rely on a single source of truth.
    // BuildFormValuesDictionary moved into FamilyService

    public FamilyController(SqlDbContext db, IFamilyService familyService)
    {
        _db = db;
        _familyService = familyService;
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(FamilyProfileResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMyFamily()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var resp = await _familyService.GetMyFamilyAsync(userId);
        if (resp == null) return NotFound();
        return Ok(resp);
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 200)]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 400)]
    public async Task<IActionResult> Register([FromBody] FamilyRegistrationRequest request)
    {
        var result = await _familyService.RegisterAsync(request);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("update")]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 200)]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 400)]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 404)]
    public async Task<IActionResult> Update([FromBody] FamilyUpdateRequest request)
    {
        var result = await _familyService.UpdateAsync(request);
        if (!result.Success)
        {
            // Decide NotFound vs BadRequest based on message patterns
            if (string.Equals(result.Message, "User not found", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.Message, "Family record not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(result);
            }
            return BadRequest(result);
        }
        return Ok(result);
    }

    // List child players for a family user within a job context, including registration status.
    // Lightweight listing of family account users (currently single-family user). Returns an array for future multi-user support.
    [HttpGet("users")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<object>), 200)]
    public async Task<IActionResult> GetFamilyUsers([FromQuery] string? jobPath)
    {
        // Caller identity
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();

        // Determine if caller has a Families record; if not, return empty list (must create one first)
        var fam = await _db.Families.SingleOrDefaultAsync(f => f.FamilyUserId == callerId);
        if (fam == null)
        {
            return Ok(Array.Empty<object>());
        }

        // Display name preference: MomFirstName/LastName then Dad fallback then username (from AspNetUsers)
        var aspUser = await _db.AspNetUsers.SingleOrDefaultAsync(u => u.Id == callerId);
        // Build a display name with clear imperative logic (avoid nested ternaries for readability / complexity)
        string display;
        if (!string.IsNullOrWhiteSpace(fam.MomFirstName) || !string.IsNullOrWhiteSpace(fam.MomLastName))
        {
            display = $"{fam.MomFirstName} {fam.MomLastName}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(fam.DadFirstName) || !string.IsNullOrWhiteSpace(fam.DadLastName))
        {
            display = $"{fam.DadFirstName} {fam.DadLastName}".Trim();
        }
        else if (aspUser != null && (!string.IsNullOrWhiteSpace(aspUser.FirstName) || !string.IsNullOrWhiteSpace(aspUser.LastName)))
        {
            var first = aspUser.FirstName ?? string.Empty;
            var last = aspUser.LastName ?? string.Empty;
            display = $"{first} {last}".Trim();
        }
        else
        {
            var userName = aspUser != null ? aspUser.UserName : null;
            display = userName ?? "Family";
        }

        var result = new[]
        {
            new { familyUserId = fam.FamilyUserId, displayName = display, userName = (aspUser != null ? (aspUser.UserName ?? string.Empty) : string.Empty) }
        };
        return Ok(result);
    }

    // ...existing code...
    [HttpGet("players")]
    [Authorize]
    [ProducesResponseType(typeof(FamilyPlayersResponseDto), 200)]
    public async Task<IActionResult> GetFamilyPlayers([FromQuery] string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
            return BadRequest(new { message = "jobPath is required" });

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId))
            return Unauthorized();

        try
        {
            var dto = await _familyService.GetFamilyPlayersAsync(familyUserId, jobPath);
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, new { message = ex.Message, jobPath });
        }
    }
    // ...existing code...
}
