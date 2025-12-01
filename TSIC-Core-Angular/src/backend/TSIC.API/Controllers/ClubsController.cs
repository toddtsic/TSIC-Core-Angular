using Microsoft.AspNetCore.Mvc;
using TSIC.API.Dtos;
using TSIC.API.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/clubs")]
public class ClubsController : ControllerBase
{
    private readonly IClubService _clubService;

    public ClubsController(IClubService clubService)
    {
        _clubService = clubService;
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(List<ClubSearchResult>), 200)]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string? state)
    {
        var results = await _clubService.SearchClubsAsync(q, state);
        return Ok(results);
    }
}

