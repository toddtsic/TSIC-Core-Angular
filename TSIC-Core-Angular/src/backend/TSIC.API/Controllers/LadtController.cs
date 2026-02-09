using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Ladt;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/ladt")]
[Authorize(Policy = "AdminOnly")]
public class LadtController : ControllerBase
{
    private readonly ILadtService _ladtService;
    private readonly IJobLookupService _jobLookupService;

    public LadtController(ILadtService ladtService, IJobLookupService jobLookupService)
    {
        _ladtService = ladtService;
        _jobLookupService = jobLookupService;
    }

    // ═══════════════════════════════════════════
    // Tree
    // ═══════════════════════════════════════════

    [HttpGet("tree")]
    public async Task<ActionResult<LadtTreeRootDto>> GetTree(CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var tree = await _ladtService.GetLadtTreeAsync(jobId!.Value, cancellationToken);
        return Ok(tree);
    }

    // ═══════════════════════════════════════════
    // League
    // ═══════════════════════════════════════════

    [HttpGet("leagues/{leagueId:guid}")]
    public async Task<ActionResult<LeagueDetailDto>> GetLeague(Guid leagueId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.GetLeagueDetailAsync(leagueId, jobId!.Value, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("leagues/{leagueId:guid}")]
    public async Task<ActionResult<LeagueDetailDto>> UpdateLeague(
        Guid leagueId, [FromBody] UpdateLeagueRequest request, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.UpdateLeagueAsync(leagueId, request, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ═══════════════════════════════════════════
    // Agegroup
    // ═══════════════════════════════════════════

    [HttpGet("agegroups/{agegroupId:guid}")]
    public async Task<ActionResult<AgegroupDetailDto>> GetAgegroup(Guid agegroupId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.GetAgegroupDetailAsync(agegroupId, jobId!.Value, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("agegroups")]
    public async Task<ActionResult<AgegroupDetailDto>> CreateAgegroup(
        [FromBody] CreateAgegroupRequest request, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.CreateAgegroupAsync(request, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("agegroups/{agegroupId:guid}")]
    public async Task<ActionResult<AgegroupDetailDto>> UpdateAgegroup(
        Guid agegroupId, [FromBody] UpdateAgegroupRequest request, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.UpdateAgegroupAsync(agegroupId, request, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("agegroups/{agegroupId:guid}")]
    public async Task<IActionResult> DeleteAgegroup(Guid agegroupId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _ladtService.DeleteAgegroupAsync(agegroupId, jobId!.Value, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("agegroups/stub/{leagueId:guid}")]
    public async Task<ActionResult<Guid>> AddStubAgegroup(Guid leagueId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var id = await _ladtService.AddStubAgegroupAsync(leagueId, jobId!.Value, userId!, cancellationToken);
            return Ok(id);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ═══════════════════════════════════════════
    // Division
    // ═══════════════════════════════════════════

    [HttpGet("divisions/{divId:guid}")]
    public async Task<ActionResult<DivisionDetailDto>> GetDivision(Guid divId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.GetDivisionDetailAsync(divId, jobId!.Value, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("divisions")]
    public async Task<ActionResult<DivisionDetailDto>> CreateDivision(
        [FromBody] CreateDivisionRequest request, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.CreateDivisionAsync(request, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("divisions/{divId:guid}")]
    public async Task<ActionResult<DivisionDetailDto>> UpdateDivision(
        Guid divId, [FromBody] UpdateDivisionRequest request, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.UpdateDivisionAsync(divId, request, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("divisions/{divId:guid}")]
    public async Task<IActionResult> DeleteDivision(Guid divId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            await _ladtService.DeleteDivisionAsync(divId, jobId!.Value, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("divisions/stub/{agegroupId:guid}")]
    public async Task<ActionResult<Guid>> AddStubDivision(Guid agegroupId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var id = await _ladtService.AddStubDivisionAsync(agegroupId, jobId!.Value, userId!, cancellationToken);
            return Ok(id);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ═══════════════════════════════════════════
    // Team
    // ═══════════════════════════════════════════

    [HttpGet("teams/{teamId:guid}")]
    public async Task<ActionResult<TeamDetailDto>> GetTeam(Guid teamId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.GetTeamDetailAsync(teamId, jobId!.Value, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("teams")]
    public async Task<ActionResult<TeamDetailDto>> CreateTeam(
        [FromBody] CreateTeamRequest request, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.CreateTeamAsync(request, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPut("teams/{teamId:guid}")]
    public async Task<ActionResult<TeamDetailDto>> UpdateTeam(
        Guid teamId, [FromBody] UpdateTeamRequest request, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.UpdateTeamAsync(teamId, request, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("teams/{teamId:guid}")]
    public async Task<ActionResult<DeleteTeamResultDto>> DeleteTeam(Guid teamId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _ladtService.DeleteTeamAsync(teamId, jobId!.Value, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("teams/{teamId:guid}/drop")]
    public async Task<ActionResult<DropTeamResultDto>> DropTeam(Guid teamId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var result = await _ladtService.DropTeamAsync(teamId, jobId!.Value, userId!, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("teams/{teamId:guid}/clone")]
    public async Task<ActionResult<TeamDetailDto>> CloneTeam(Guid teamId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var detail = await _ladtService.CloneTeamAsync(teamId, jobId!.Value, userId!, cancellationToken);
            return Ok(detail);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("teams/stub/{divId:guid}")]
    public async Task<ActionResult<Guid>> AddStubTeam(Guid divId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var id = await _ladtService.AddStubTeamAsync(divId, jobId!.Value, userId!, cancellationToken);
            return Ok(id);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ═══════════════════════════════════════════
    // Sibling Batch Queries
    // ═══════════════════════════════════════════

    [HttpGet("leagues/siblings")]
    public async Task<ActionResult<List<LeagueDetailDto>>> GetLeagueSiblings(CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var list = await _ladtService.GetLeagueSiblingsAsync(jobId!.Value, cancellationToken);
        return Ok(list);
    }

    [HttpGet("agegroups/by-league/{leagueId:guid}")]
    public async Task<ActionResult<List<AgegroupDetailDto>>> GetAgegroupsByLeague(Guid leagueId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var list = await _ladtService.GetAgegroupsByLeagueAsync(leagueId, jobId!.Value, cancellationToken);
            return Ok(list);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("divisions/by-agegroup/{agegroupId:guid}")]
    public async Task<ActionResult<List<DivisionDetailDto>>> GetDivisionsByAgegroup(Guid agegroupId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var list = await _ladtService.GetDivisionsByAgegroupAsync(agegroupId, jobId!.Value, cancellationToken);
            return Ok(list);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("teams/by-division/{divId:guid}")]
    public async Task<ActionResult<List<TeamDetailDto>>> GetTeamsByDivision(Guid divId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var list = await _ladtService.GetTeamsByDivisionAsync(divId, jobId!.Value, cancellationToken);
            return Ok(list);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ═══════════════════════════════════════════
    // Batch Operations
    // ═══════════════════════════════════════════

    [HttpPost("batch/waitlist-agegroups")]
    public async Task<ActionResult<int>> AddWaitlistAgegroups(CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        var count = await _ladtService.AddWaitlistAgegroupsAsync(jobId!.Value, userId!, cancellationToken);
        return Ok(count);
    }

    [HttpPost("batch/update-fees/{agegroupId:guid}")]
    public async Task<ActionResult<int>> UpdatePlayerFeesToAgegroupFees(Guid agegroupId, CancellationToken cancellationToken)
    {
        var (jobId, userId, error) = await ResolveContext();
        if (error != null) return error;

        try
        {
            var count = await _ladtService.UpdatePlayerFeesToAgegroupFeesAsync(agegroupId, jobId!.Value, cancellationToken);
            return Ok(count);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ═══════════════════════════════════════════
    // Shared context resolution
    // ═══════════════════════════════════════════

    private async Task<(Guid? jobId, string? userId, ActionResult? error)> ResolveContext()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return (null, null, BadRequest(new { message = "Registration context required" }));

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return (null, null, Unauthorized());

        return (jobId, userId, null);
    }
}
