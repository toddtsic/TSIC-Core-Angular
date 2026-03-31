using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Team attendance tracking — events, RSVP, and player history.
/// </summary>
[ApiController]
[Authorize]
[Route("api/teams")]
public class TeamAttendanceController : ControllerBase
{
    private readonly ITeamAttendanceService _attendanceService;

    public TeamAttendanceController(ITeamAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [HttpGet("{teamId:guid}/attendance/events")]
    [ProducesResponseType(typeof(List<AttendanceEventDto>), 200)]
    public async Task<IActionResult> GetEvents(Guid teamId, CancellationToken ct)
    {
        var events = await _attendanceService.GetEventsAsync(teamId, ct);
        return Ok(events);
    }

    [HttpPost("{teamId:guid}/attendance/events")]
    [ProducesResponseType(typeof(AttendanceEventDto), 201)]
    public async Task<IActionResult> CreateEvent(
        Guid teamId, [FromBody] CreateAttendanceEventRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var evt = await _attendanceService.CreateEventAsync(teamId, userId, request, ct);
        return CreatedAtAction(nameof(GetEvents), new { teamId }, evt);
    }

    [HttpDelete("{teamId:guid}/attendance/events/{eventId:int}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteEvent(Guid teamId, int eventId, CancellationToken ct)
    {
        var deleted = await _attendanceService.DeleteEventAsync(eventId, ct);
        return deleted ? Ok() : NotFound();
    }

    [HttpGet("{teamId:guid}/attendance/events/{eventId:int}/roster")]
    [ProducesResponseType(typeof(List<AttendanceRosterDto>), 200)]
    public async Task<IActionResult> GetEventRoster(Guid teamId, int eventId, CancellationToken ct)
    {
        var roster = await _attendanceService.GetEventRosterAsync(eventId, ct);
        return Ok(roster);
    }

    [HttpPost("{teamId:guid}/attendance/events/{eventId:int}/rsvp")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> UpdateRsvp(
        Guid teamId, int eventId, [FromBody] UpdateRsvpRequest request, CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        await _attendanceService.UpdateRsvpAsync(eventId, request, userId, ct);
        return Ok();
    }

    [HttpGet("{teamId:guid}/attendance/player/{userId}/history")]
    [ProducesResponseType(typeof(List<AttendanceHistoryDto>), 200)]
    public async Task<IActionResult> GetPlayerHistory(Guid teamId, string userId, CancellationToken ct)
    {
        var history = await _attendanceService.GetPlayerHistoryAsync(teamId, userId, ct);
        return Ok(history);
    }

    [HttpGet("attendance/event-types")]
    [ProducesResponseType(typeof(List<AttendanceEventTypeDto>), 200)]
    public async Task<IActionResult> GetEventTypes(CancellationToken ct)
    {
        var types = await _attendanceService.GetEventTypesAsync(ct);
        return Ok(types);
    }
}
