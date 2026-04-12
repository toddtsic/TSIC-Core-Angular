using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Lightweight self-roster update: lets families change uniform #, position, team,
/// or delete a player registration post-payment — without re-entering the full wizard.
/// </summary>
[ApiController]
[Route("api/self-roster-update")]
[Authorize]
public class SelfRosterUpdateController : ControllerBase
{
    private readonly IRegistrationRepository _registrations;
    private readonly ITeamRepository _teams;
    private readonly IDdlOptionsService _ddlOptions;
    private readonly IJobLookupService _jobLookup;
    private readonly ILogger<SelfRosterUpdateController> _logger;

    public SelfRosterUpdateController(
        IRegistrationRepository registrations,
        ITeamRepository teams,
        IDdlOptionsService ddlOptions,
        IJobLookupService jobLookup,
        ILogger<SelfRosterUpdateController> logger)
    {
        _registrations = registrations;
        _teams = teams;
        _ddlOptions = ddlOptions;
        _jobLookup = jobLookup;
        _logger = logger;
    }

    /// <summary>
    /// Get all registered players for the current family + job, with available teams and positions.
    /// </summary>
    [HttpGet("players")]
    [ProducesResponseType(typeof(List<SelfRosterPlayerDto>), 200)]
    public async Task<IActionResult> GetPlayers(CancellationToken ct)
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookup);
        if (jobId == null) return BadRequest(new { message = "Registration context required" });

        // Load family's registrations for this job (includes User navigation for names)
        var allRegs = await _registrations.GetByJobAndFamilyWithUsersAsync(
            jobId.Value, familyUserId, activePlayersOnly: true, ct);

        // Filter to active players with assigned teams
        var activeRegs = allRegs
            .Where(r => r.BActive == true && r.AssignedTeamId.HasValue && r.RoleId == "Player")
            .ToList();

        if (activeRegs.Count == 0)
            return Ok(new List<SelfRosterPlayerDto>());

        // Load available teams + positions for dropdowns
        var availableTeams = await _teams.GetAvailableTeamsQueryResultsAsync(jobId.Value, ct);
        var rosterCounts = await _registrations.GetActiveTeamRosterCountsAsync(
            jobId.Value,
            availableTeams.Select(t => t.TeamId).ToList(),
            ct);

        var teamOptions = availableTeams
            .Where(t => (t.TeamAllowsSelfRostering ?? true) && (t.AgegroupAllowsSelfRostering ?? true))
            .Select(t => new SelfRosterTeamOptionDto
            {
                TeamId = t.TeamId,
                TeamName = t.Name,
                CurrentCount = rosterCounts.TryGetValue(t.TeamId, out var cnt) ? cnt : 0,
                MaxCount = t.MaxCount
            })
            .OrderBy(t => t.TeamName)
            .ToList();

        var ddlOptions = await _ddlOptions.GetOptionsAsync(jobId.Value, ct);
        var positions = ddlOptions.Positions ?? new List<string>();

        // Build team name lookup for current assignments
        var teamNameMap = availableTeams.ToDictionary(t => t.TeamId, t => t.Name);

        var players = activeRegs.Select(r => new SelfRosterPlayerDto
        {
            RegistrationId = r.RegistrationId,
            FirstName = r.User?.FirstName ?? string.Empty,
            LastName = r.User?.LastName ?? string.Empty,
            UniformNo = r.UniformNo,
            Position = r.Position,
            TeamId = r.AssignedTeamId!.Value,
            TeamName = teamNameMap.TryGetValue(r.AssignedTeamId!.Value, out var name) ? name : "Unknown",
            AvailableTeams = teamOptions,
            AvailablePositions = positions
        }).ToList();

        return Ok(players);
    }

    /// <summary>
    /// Update a player's uniform #, position, and/or team assignment.
    /// </summary>
    [HttpPut("{registrationId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdatePlayer(
        Guid registrationId,
        [FromBody] SelfRosterUpdateRequestDto request,
        CancellationToken ct)
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var reg = await _registrations.GetByIdAsync(registrationId, ct);
        if (reg == null) return NotFound(new { message = "Registration not found" });
        if (reg.FamilyUserId != familyUserId) return Forbid();

        // Look up team name for the Assignment field
        var team = await _teams.GetTeamFromTeamId(request.TeamId);
        var teamName = team?.TeamName ?? "Unknown";

        reg.UniformNo = request.UniformNo;
        reg.Position = request.Position;
        reg.AssignedTeamId = request.TeamId;
        reg.Assignment = $"Player: {teamName}";
        reg.Modified = DateTime.UtcNow;

        _registrations.Update(reg);
        await _registrations.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[SelfRosterUpdate] Updated registration {RegId} — Uniform: {Uniform}, Position: {Position}, Team: {TeamId}",
            registrationId, request.UniformNo, request.Position, request.TeamId);

        return Ok(new { message = "Registration updated" });
    }

    /// <summary>
    /// Hard-delete a player's registration.
    /// </summary>
    [HttpDelete("{registrationId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeletePlayer(Guid registrationId, CancellationToken ct)
    {
        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId)) return Unauthorized();

        var reg = await _registrations.GetByIdAsync(registrationId, ct);
        if (reg == null) return NotFound(new { message = "Registration not found" });
        if (reg.FamilyUserId != familyUserId) return Forbid();

        _registrations.Remove(reg);
        await _registrations.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[SelfRosterUpdate] Deleted registration {RegId} for player {UserId}",
            registrationId, reg.UserId);

        return NoContent();
    }
}
