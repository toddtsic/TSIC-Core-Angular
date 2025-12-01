using Microsoft.AspNetCore.Mvc;
using TSIC.API.Dtos;
using TSIC.API.Services;

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
    public async Task<IActionResult> Register([FromBody] ClubRepRegistrationRequest request)
    {
        var result = await _clubService.RegisterAsync(request);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
