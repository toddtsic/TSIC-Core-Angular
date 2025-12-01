using Microsoft.AspNetCore.Mvc;
using TSIC.API.Dtos;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClubsController : ControllerBase
{
    private readonly IClubService _clubService;

    public ClubsController(IClubService clubService)
    {
        _clubService = clubService;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(ClubRegistrationResponse), 200)]
    [ProducesResponseType(typeof(ClubRegistrationResponse), 400)]
    public async Task<IActionResult> Register([FromBody] ClubRegistrationRequest request)
    {
        var result = await _clubService.RegisterAsync(request);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(List<ClubSearchResult>), 200)]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string? state)
    {
        var results = await _clubService.SearchClubsAsync(q, state);
        return Ok(results);
    }
}

