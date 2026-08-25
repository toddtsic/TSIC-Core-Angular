using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.Contracts.Dtos.Arb;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Manual trigger for the daily ADN reconciliation sweep. Mirrors legacy
/// AdnArbSweepController.FindImportEmailAllRequest — reuses the same
/// IAdnSweepService that the BackgroundService runs nightly.
///
/// MODE FOLLOWS THE ENVIRONMENT, and there is no way to ask for the other one. On Production this
/// runs the real sweep: money books, families are emailed, the digest is mailed. Everywhere else it
/// runs a DRY RUN — real production batches are read, every failed draft is resolved exactly as the
/// live pass would, and nothing is written, settled, or sent. Neither mode is selectable, because a
/// selectable mode is one that can be selected wrongly, and being wrong on Production means a
/// morning's money silently not booked.
/// </summary>
[ApiController]
[Route("api/admin/adn-sweep")]
[Authorize(Policy = "SuperUserOnly")]
public class AdnSweepController : ControllerBase
{
    private readonly IAdnSweepService _sweep;
    private readonly IArbNotificationService _arbNotify;
    private readonly IHostEnvironment _env;

    public AdnSweepController(
        IAdnSweepService sweep,
        IArbNotificationService arbNotify,
        IHostEnvironment env)
    {
        _sweep = sweep;
        _arbNotify = arbNotify;
        _env = env;
    }

    /// <summary>
    /// What this host will do if you press the button. Read by the screen so it can say so BEFORE
    /// the click rather than reporting it afterwards.
    /// </summary>
    [HttpGet("mode")]
    public ActionResult<AdnSweepModeDto> Mode() => Ok(new AdnSweepModeDto
    {
        DryRun = _env.IsSandbox(),
        EnvironmentName = _env.EnvironmentName,
        MachineName = System.Environment.MachineName
    });

    /// <summary>
    /// Run a manual sweep pass right now. Optional daysPrior overrides the configured window.
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<AdnSweepResult>> Run([FromQuery] int daysPrior = 0, CancellationToken ct = default)
    {
        // Live runs mail their own digest, exactly as before. A dry run suppresses the send inside the
        // service and hands the same HTML back on DigestHtml.
        var result = await _sweep.RunAsync("Manual", daysPrior, sendDigest: true, ct);
        return Ok(result);
    }

    /// <summary>
    /// Dry-run the expiring-card pass that fires unattended on the 2nd and the 15th.
    ///
    /// NOT AVAILABLE ON PRODUCTION — deliberately. That pass mails thousands of families across every
    /// job holding a live subscription, and it is scheduled precisely so that no one has to decide to
    /// send it. A hand trigger on Production would be a way to send it twice, or on the wrong day, with
    /// no undo. On Production it stays on the timer; here it renders.
    ///
    /// Reads REAL production Authorize.Net either way — GetExpiringCardFlagsAsync forces the production
    /// account, since expiring cards exist only there — so this shows the actual families in scope.
    /// </summary>
    [HttpPost("expiring-cards/dry-run")]
    public async Task<ActionResult<ArbNotifyResultDto>> ExpiringCardsDryRun(CancellationToken ct = default)
    {
        if (!_env.IsSandbox()) return NotFound();

        var result = await _arbNotify.NotifyExpiringCardsAsync(ct);
        return Ok(result);
    }
}

/// <summary>Which mode this host runs the sweep in, and the identity behind that answer.</summary>
public sealed record AdnSweepModeDto
{
    public required bool DryRun { get; init; }
    public required string EnvironmentName { get; init; }
    public required string MachineName { get; init; }
}
