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
using TSIC.API.Services.External;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Email;
using TSIC.API.Services.Validation;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/club-reps")]
public class ClubRepsController : ControllerBase
{
    private readonly IClubService _clubService;

    public ClubRepsController(IClubService clubService)
    {
        _clubService = clubService;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(ClubRepRegistrationResponse), 200)]
    [ProducesResponseType(typeof(ClubRepRegistrationResponse), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 500)]
    public async Task<IActionResult> Register([FromBody] ClubRepRegistrationRequest request)
    {
        try
        {
            var result = await _clubService.RegisterAsync(request);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
        catch (Exception ex)
        {
            // Return structured error for client consumption
            return StatusCode(500, new ProblemDetails
            {
                Status = 500,
                Title = "Club Registration Failed",
                Detail = ex.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            });
        }
    }
}
