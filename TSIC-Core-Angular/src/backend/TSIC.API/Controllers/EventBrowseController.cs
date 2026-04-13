using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Public event browse/discovery endpoints. No authentication required.
/// Used by mobile apps to list events, view alerts, docs, and game clock config.
/// </summary>
[ApiController]
[Route("api/events")]
public class EventBrowseController : ControllerBase
{
    private readonly IEventBrowseService _eventBrowseService;

    public EventBrowseController(IEventBrowseService eventBrowseService)
    {
        _eventBrowseService = eventBrowseService;
    }

    /// <summary>
    /// List all active public events (not expired, not suspended, public schedule access enabled).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<EventListingDto>), 200)]
    public async Task<IActionResult> GetActiveEvents(CancellationToken ct)
    {
        var events = await _eventBrowseService.GetActiveEventsAsync(ct);
        return Ok(events);
    }

    /// <summary>
    /// Get push notification alerts for an event (newest first).
    /// </summary>
    [HttpGet("{jobId:guid}/alerts")]
    [ProducesResponseType(typeof(List<EventAlertDto>), 200)]
    public async Task<IActionResult> GetAlerts(Guid jobId, CancellationToken ct)
    {
        var alerts = await _eventBrowseService.GetAlertsAsync(jobId, ct);
        return Ok(alerts);
    }

    /// <summary>
    /// Get job-level documents and links for an event.
    /// </summary>
    [HttpGet("{jobId:guid}/docs")]
    [ProducesResponseType(typeof(List<EventDocDto>), 200)]
    public async Task<IActionResult> GetDocs(Guid jobId, CancellationToken ct)
    {
        var docs = await _eventBrowseService.GetDocsAsync(jobId, ct);
        return Ok(docs);
    }

    /// <summary>
    /// Get game clock timing configuration for an event.
    /// </summary>
    [HttpGet("{jobId:guid}/game-clock")]
    [ProducesResponseType(typeof(GameClockConfigDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetGameClock(Guid jobId, CancellationToken ct)
    {
        var config = await _eventBrowseService.GetGameClockConfigAsync(jobId, ct);
        if (config == null)
            return NotFound(new { Error = "No game clock configuration found for this event" });
        return Ok(config);
    }

    /// <summary>
    /// Get currently-live or next-upcoming games for an event (drives countdown-clock UI).
    /// </summary>
    [HttpGet("{jobId:guid}/active-games")]
    [ProducesResponseType(typeof(GameClockAvailableGameTimesDto), 200)]
    public async Task<IActionResult> GetActiveGames(
        Guid jobId,
        [FromQuery] DateTime? preferredGameDate,
        CancellationToken ct)
    {
        var result = await _eventBrowseService.GetActiveGamesAsync(jobId, preferredGameDate, ct);
        return Ok(result);
    }
}
