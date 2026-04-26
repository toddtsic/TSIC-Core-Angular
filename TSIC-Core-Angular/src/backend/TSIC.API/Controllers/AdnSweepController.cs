using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Manual trigger for the daily ADN reconciliation sweep. Mirrors legacy
/// AdnArbSweepController.FindImportEmailAllRequest — reuses the same
/// IAdnSweepService that the BackgroundService runs nightly.
/// </summary>
[ApiController]
[Route("api/admin/adn-sweep")]
[Authorize(Policy = "SuperUserOnly")]
public class AdnSweepController : ControllerBase
{
    private readonly IAdnSweepService _sweep;

    public AdnSweepController(IAdnSweepService sweep)
    {
        _sweep = sweep;
    }

    /// <summary>
    /// Run a manual sweep pass right now. Optional daysPrior overrides the configured window.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<AdnSweepResult>> Run([FromQuery] int daysPrior = 0, CancellationToken ct = default)
    {
        var result = await _sweep.RunAsync("Manual", daysPrior, ct);
        return Ok(result);
    }
}
