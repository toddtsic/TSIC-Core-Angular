using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.CampGroups;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Camp Day/Night Groups admin — replaces legacy Rosters/DayNightGroups.
/// Lets a director list teams, list a team's campers, and assign Day/Night group
/// per registrant or in bulk. Group dropdown values come from Jobs.JsonOptions
/// (managed via Configure Job → DDL Options).
/// </summary>
[ApiController]
[Route("api/camp-groups")]
[Authorize(Policy = "AdminOnly")]
public class CampGroupsController : ControllerBase
{
    private readonly ICampGroupsService _service;
    private readonly ITeamRepository _teamRepo;
    private readonly IJobLookupService _jobLookupService;

    public CampGroupsController(
        ICampGroupsService service,
        ITeamRepository teamRepo,
        IJobLookupService jobLookupService)
    {
        _service = service;
        _teamRepo = teamRepo;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// GET /api/camp-groups/teams — All active teams in the current job with player counts.
    /// </summary>
    [HttpGet("teams")]
    public async Task<ActionResult<List<TeamRosterCountDto>>> GetTeams(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var teams = await _service.GetTeamsAsync(jobId.Value, ct);
        return Ok(teams);
    }

    /// <summary>
    /// GET /api/camp-groups/options — Day/Night group dropdown values for the current
    /// job. AdminOnly read of a slice of JsonOptions (DDL Options editor itself is SU).
    /// </summary>
    [HttpGet("options")]
    public async Task<ActionResult<CampGroupOptionsDto>> GetOptions(CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var options = await _service.GetGroupOptionsAsync(jobId.Value, ct);
        return Ok(options);
    }

    /// <summary>
    /// GET /api/camp-groups/teams/{teamId}/campers — Active Player registrations on
    /// a team, with current Day/Night group values. Team must belong to the caller's job.
    /// </summary>
    [HttpGet("teams/{teamId:guid}/campers")]
    public async Task<ActionResult<List<CampPlayerDto>>> GetCampers(Guid teamId, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        var belongs = await _teamRepo.BelongsToJobAsync(teamId, jobId.Value, ct);
        if (!belongs)
            return NotFound(new { message = "Team not found in current job." });

        var campers = await _service.GetCampersAsync(teamId, ct);
        return Ok(campers);
    }

    /// <summary>
    /// PATCH /api/camp-groups/registrations/{regId} — Update Day and/or Night group on
    /// a single registration. Set the matching `Update*Group` flag for each field you
    /// want to write. Empty string is normalized to null.
    /// </summary>
    [HttpPatch("registrations/{regId:guid}")]
    public async Task<IActionResult> UpdateGroups(
        Guid regId,
        [FromBody] UpdateCampGroupsRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        if (!request.UpdateDayGroup && !request.UpdateNightGroup)
            return BadRequest(new { message = "No fields to update." });

        var touched = await _service.UpdateGroupsAsync(jobId.Value, regId, request, ct);
        if (!touched)
            return NotFound(new { message = "Registration not found in current job." });

        return NoContent();
    }

    /// <summary>
    /// PATCH /api/camp-groups/registrations/bulk — Apply the same Day and/or Night group
    /// value across many registrations. Useful for "set all selected to X" from the
    /// admin screen's header bar. Returns the count of rows touched.
    /// </summary>
    [HttpPatch("registrations/bulk")]
    public async Task<ActionResult<BulkUpdateCampGroupsResponse>> BulkUpdateGroups(
        [FromBody] BulkUpdateCampGroupsRequest request,
        CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId is null)
            return BadRequest(new { message = "Registration context required." });

        if (request.RegistrationIds.Count == 0)
            return BadRequest(new { message = "RegistrationIds must not be empty." });

        if (!request.UpdateDayGroup && !request.UpdateNightGroup)
            return BadRequest(new { message = "No fields to update." });

        var updated = await _service.BulkUpdateGroupsAsync(jobId.Value, request, ct);
        return Ok(new BulkUpdateCampGroupsResponse { UpdatedCount = updated });
    }
}
