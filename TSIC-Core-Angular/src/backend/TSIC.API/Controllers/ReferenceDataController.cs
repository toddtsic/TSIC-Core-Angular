using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.Reference;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Static lookup lists from the `reference` schema.
///
/// [AllowAnonymous] is load-bearing: the store walk-up form and the family address step are
/// anonymous surfaces. An authenticated endpoint here would serve them an empty dropdown —
/// which is the exact bug this controller exists to fix.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/reference")]
public class ReferenceDataController : ControllerBase
{
    private readonly IReferenceDataService _service;

    public ReferenceDataController(IReferenceDataService service)
    {
        _service = service;
    }

    /// <summary>
    /// Every state, territory and Canadian province, ordered by display name.
    /// The `value` is the 2-char code stored on the account.
    /// </summary>
    [HttpGet("states")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(List<StateOptionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StateOptionDto>>> GetStates(CancellationToken ct)
    {
        var states = await _service.GetStatesAsync(ct);
        return Ok(states);
    }
}
