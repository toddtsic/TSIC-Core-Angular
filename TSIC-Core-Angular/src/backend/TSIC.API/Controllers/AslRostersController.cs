using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// Anonymous ASL roster board — the public, hard-branded region/team card view that American Select
/// links in from its own site and screenshots into social feeds.
///
/// Ported from legacy ASLRostersController (TSIC-Unify). Behavior is deliberately legacy-identical:
/// region is derived from the team name, and Teams.team_comments both supplies the coaches line and
/// gates whether a team appears at all.
/// </summary>
[ApiController]
[Route("api/asl-rosters")]
[AllowAnonymous]
public class AslRostersController : ControllerBase
{
    private readonly ITeamRepository _teamRepository;
    private readonly IJobLookupService _jobLookupService;

    public AslRostersController(
        ITeamRepository teamRepository,
        IJobLookupService jobLookupService)
    {
        _teamRepository = teamRepository;
        _jobLookupService = jobLookupService;
    }

    /// <summary>GET /api/asl-rosters/index?jobPath= — region list + full team list for the dropdowns.</summary>
    [HttpGet("index")]
    [ProducesResponseType(typeof(AslRostersIndexDto), 200)]
    public async Task<IActionResult> GetIndex([FromQuery] string jobPath, CancellationToken ct)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
            return NotFound(new { message = "Event not found" });

        var teams = await _teamRepository.GetAslRosterTeamsAsync(jobId.Value, null, ct);

        var regions = teams
            .Select(t => AslRegionConstants.ResolveRegion(t.TeamName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new AslRostersIndexDto
        {
            Regions = regions,
            Teams = teams
                .Select(t => new AslTeamMenuItemDto { TeamId = t.TeamId, TeamName = t.TeamName })
                .ToList()
        });
    }

    /// <summary>GET /api/asl-rosters/region?jobPath=&amp;region= — every team card in one region.</summary>
    [HttpGet("region")]
    [ProducesResponseType(typeof(List<AslRegionTeamDto>), 200)]
    public async Task<IActionResult> GetRegionRoster(
        [FromQuery] string jobPath, [FromQuery] string region, CancellationToken ct)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
            return NotFound(new { message = "Event not found" });

        if (string.IsNullOrWhiteSpace(region))
            return Ok(new List<AslRegionTeamDto>());

        var teams = await _teamRepository.GetAslRosterTeamsAsync(jobId.Value, region, ct);
        var cards = await BuildCardsAsync(jobId.Value, teams, region, ct);
        return Ok(cards);
    }

    /// <summary>GET /api/asl-rosters/team/{teamId}?jobPath= — a single team card.</summary>
    [HttpGet("team/{teamId:guid}")]
    [ProducesResponseType(typeof(AslRegionTeamDto), 200)]
    public async Task<IActionResult> GetTeamRoster(
        Guid teamId, [FromQuery] string jobPath, CancellationToken ct)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
            return NotFound(new { message = "Event not found" });

        // Legacy queried on teamId alone, so any team GUID from any job returned its roster to an
        // anonymous caller. Scope to the job and let an unknown team 404 rather than throw.
        var teams = await _teamRepository.GetAslRosterTeamsAsync(jobId.Value, null, ct);
        var team = teams.FirstOrDefault(t => t.TeamId == teamId);
        if (team == null)
            return NotFound(new { message = "Team not found." });

        var cards = await BuildCardsAsync(jobId.Value, [team], region: null, ct);
        return Ok(cards[0]);
    }

    /// <summary>
    /// Attach rosters to team rows in one round trip. Legacy issued a query per team; a big region
    /// carries a dozen teams, so that was a dozen round trips per dropdown change.
    /// </summary>
    private async Task<List<AslRegionTeamDto>> BuildCardsAsync(
        Guid jobId, IReadOnlyList<AslTeamRowDto> teams, string? region, CancellationToken ct)
    {
        var teamIds = teams.Select(t => t.TeamId).ToList();
        var playerRows = await _teamRepository.GetAslTeamPlayersAsync(jobId, teamIds, ct);

        var byTeam = playerRows
            .GroupBy(p => p.TeamId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AslTeamPlayerDto>)g.Select(p => p.Player).ToList());

        return teams
            .Select(t => new AslRegionTeamDto
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                TeamRegion = region ?? AslRegionConstants.ResolveRegion(t.TeamName),
                GradYear = AslRegionConstants.ResolveGradYear(t.TeamName),
                TeamCoaches = t.TeamCoaches,
                ListTeamPlayers = byTeam.TryGetValue(t.TeamId, out var players) ? players : []
            })
            .ToList();
    }
}
