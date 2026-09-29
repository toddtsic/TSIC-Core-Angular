using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.TeamTournaments;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// The TSIC-Teams Schedules tab. {teamId} is ALWAYS the app's own club-job team; every other id
/// on these routes is the TOURNAMENT's (tournamentJobId, tournamentTeamId). The club job is
/// never addressed by id here -- it is only where the club name is derived from.
///
/// Flow: list tournaments -> match the team in one -> its games. Standings, brackets, the
/// standings team modal and field info are the EXISTING view-schedule by-team / by-id calls,
/// keyed on the tournament team, exactly as TSIC-Events makes them.
///
/// THREE CALLER GATES, IN THIS ORDER, ON EVERY ACTION (as Team Chat):
///   1. cross-job   -- is this team in the caller's event
///   2. reach       -- Director and Superuser reach every team; everyone else their own
///   3. enablement  -- teamevents.JobFeatures.ScheduleEnabled for the club job
/// Then the service re-runs the tournament's availability gates; a failure there is the same
/// "Schedule not available" 404 view-schedule answers with.
/// </summary>
[ApiController]
[Authorize]
[Route("api/teams/{teamId:guid}/tournaments")]
public class TeamTournamentsController : ControllerBase
{
    private readonly ITeamTournamentsService _tournaments;
    private readonly IJobLookupService _jobLookupService;

    public TeamTournamentsController(ITeamTournamentsService tournaments, IJobLookupService jobLookupService)
    {
        _tournaments = tournaments;
        _jobLookupService = jobLookupService;
    }

    /// <summary>Tournaments with the club's teams scheduled, public and announced, a game still ahead.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<TeamTournamentDto>), 200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetTournaments(Guid teamId, CancellationToken ct)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;

        return Ok(await _tournaments.GetTournamentsAsync(teamId, ct));
    }

    /// <summary>The single matched tournament team, or the club's teams in it to pick from.</summary>
    [HttpGet("{tournamentJobId:guid}/match")]
    [ProducesResponseType(typeof(TeamTournamentMatchDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Match(Guid teamId, Guid tournamentJobId, CancellationToken ct)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;

        var result = await _tournaments.MatchAsync(teamId, tournamentJobId, ct);
        return result == null ? ViewScheduleController.ScheduleNotAvailable() : Ok(result);
    }

    /// <summary>
    /// The tournament team's games -- the same rows, order and bracket handling TSIC-Events gets
    /// for a team filter. Paging is opt-in via skip/take; X-Total-Count carries the total.
    /// </summary>
    [HttpGet("{tournamentJobId:guid}/teams/{tournamentTeamId:guid}/games")]
    [ProducesResponseType(typeof(List<ViewGameDto>), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetGames(
        Guid teamId,
        Guid tournamentJobId,
        Guid tournamentTeamId,
        CancellationToken ct,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null)
    {
        if (await DenyIfNotPermitted(teamId, ct) is { } denied) return denied;

        var result = await _tournaments.GetGamesAsync(teamId, tournamentJobId, tournamentTeamId, skip, take, ct);
        if (result == null) return ViewScheduleController.ScheduleNotAvailable();

        Response.Headers["X-Total-Count"] = result.Value.TotalCount.ToString();
        return Ok(result.Value.Games);
    }

    // ── Gates ───────────────────────────────────────────────────────────────────────────

    private async Task<IActionResult?> DenyIfNotPermitted(Guid teamId, CancellationToken ct)
    {
        if (await DenyIfCrossJob(teamId, ct) is { } crossJob) return crossJob;
        if (await DenyIfOutsideReach(teamId, ct) is { } reach) return reach;
        if (await DenyIfScheduleDisabled(teamId, ct) is { } disabled) return disabled;
        return null;
    }

    /// <summary>Rejects a teamId belonging to another job. Superuser exempt.</summary>
    private async Task<IActionResult?> DenyIfCrossJob(Guid teamId, CancellationToken ct)
    {
        if (User.IsInRole(RoleConstants.Names.SuperuserName)) return null;

        var callerJobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        var teamJobId = await _jobLookupService.GetJobIdByTeamAsync(teamId, ct);

        // Fail closed: unresolvable caller job (phase-1 token, no regId) or unknown team.
        if (callerJobId == null || teamJobId == null || callerJobId.Value != teamJobId.Value)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "TeamJobMismatch",
                Title = "Team Access Denied",
                Detail = "This team belongs to a different event than the one you are logged into."
            });

        return null;
    }

    /// <summary>Director and Superuser reach every team in the job; everyone else their own.</summary>
    private async Task<IActionResult?> DenyIfOutsideReach(Guid teamId, CancellationToken ct)
    {
        if (User.IsInRole(RoleConstants.Names.SuperuserName)
            || User.IsInRole(RoleConstants.Names.DirectorName)) return null;

        var ownTeamId = await User.GetTeamIdFromRegistrationAsync(_jobLookupService, ct);

        // Fail closed: unresolvable registration, inactive registration, or unrostered caller.
        if (ownTeamId == null || ownTeamId.Value != teamId)
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = "TeamReachDenied",
                Title = "Team Access Denied",
                Detail = "You can only view your own team's tournament schedules."
            });

        return null;
    }

    /// <summary>The club job's switch, enforced here however the client is configured.</summary>
    private async Task<IActionResult?> DenyIfScheduleDisabled(Guid teamId, CancellationToken ct)
    {
        if (await _tournaments.IsEnabledAsync(teamId, ct)) return null;

        return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Type = "TeamScheduleNotEnabled",
            Title = "Schedules Not Available",
            Detail = "Tournament schedules are not turned on for this event."
        });
    }
}
