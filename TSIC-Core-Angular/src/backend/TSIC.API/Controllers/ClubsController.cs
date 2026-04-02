using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

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

    /// <summary>
    /// Live typeahead search for clubs by name. Supports debounced frontend queries.
    /// Uses composite scoring (Levenshtein + token/Jaccard) with mega-club detection.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(List<ClubSearchResult>), 200)]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] string? state,
        CancellationToken cancellationToken)
    {
        var results = await _clubService.SearchClubsAsync(q, state);
        return Ok(results);
    }
}

