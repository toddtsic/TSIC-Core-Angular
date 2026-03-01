using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

/// <summary>
/// Dev-only scheduling utilities. Guarded by IsDevelopment() check.
/// </summary>
[ApiController]
[Route("api/dev-scheduling")]
[Authorize(Policy = "AdminOnly")]
public class DevSchedulingController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IJobLookupService _jobLookupService;
    private readonly IAutoBuildScheduleService _autoBuildService;
    private readonly ITimeslotRepository _tsRepo;
    private readonly IPairingsRepository _pairingsRepo;
    private readonly IDivisionProfileRepository _profileRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly ILogger<DevSchedulingController> _logger;

    public DevSchedulingController(
        IWebHostEnvironment env,
        IJobLookupService jobLookupService,
        IAutoBuildScheduleService autoBuildService,
        ITimeslotRepository tsRepo,
        IPairingsRepository pairingsRepo,
        IDivisionProfileRepository profileRepo,
        ISchedulingContextResolver contextResolver,
        ILogger<DevSchedulingController> logger)
    {
        _env = env;
        _jobLookupService = jobLookupService;
        _autoBuildService = autoBuildService;
        _tsRepo = tsRepo;
        _pairingsRepo = pairingsRepo;
        _profileRepo = profileRepo;
        _contextResolver = contextResolver;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/dev-scheduling/reset — Clear all scheduling config for the current job.
    /// Deletes: games, timeslot dates, timeslot fields, pairings, strategy profiles.
    /// Development environment ONLY.
    /// </summary>
    [HttpPost("reset")]
    public async Task<ActionResult> Reset(CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        _logger.LogWarning(
            "DEV RESET executing — clearing all scheduling config. JobId={JobId}",
            jobId);

        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId.Value, ct);

        // 1. Delete all games
        var gamesDeleted = await _autoBuildService.UndoAsync(jobId.Value, ct);

        // 2. Delete strategy profiles
        await _profileRepo.DeleteByJobIdAsync(jobId.Value, ct);
        await _profileRepo.SaveChangesAsync(ct);

        // 3. Get all agegroup IDs that have timeslot config (scoped to this job's league)
        var agIdsWithDates = await _tsRepo.GetAgegroupIdsWithDatesAsync(leagueId, season, year, ct);
        var agIdsWithFields = await _tsRepo.GetAgegroupIdsWithFieldTimeslotsAsync(leagueId, season, year, ct);
        var allAgIds = agIdsWithDates.Union(agIdsWithFields).ToList();

        // 4. Delete timeslot dates and fields for each agegroup (sequential — shared DbContext)
        foreach (var agId in allAgIds)
        {
            await _tsRepo.DeleteAllDatesAsync(agId, season, year, ct);
            await _tsRepo.DeleteAllFieldTimeslotsAsync(agId, season, year, ct);
        }
        await _tsRepo.SaveChangesAsync(ct);

        // 5. Delete pairings — get distinct pool sizes (team counts), then delete each
        var teamCounts = await _pairingsRepo.GetDistinctPoolSizesWithPairingsAsync(leagueId, season, ct);
        foreach (var tCnt in teamCounts)
        {
            await _pairingsRepo.DeleteAllAsync(leagueId, season, tCnt, ct);
        }
        await _pairingsRepo.SaveChangesAsync(ct);

        _logger.LogWarning(
            "DEV RESET complete. JobId={JobId}, GamesDeleted={Games}, " +
            "AgegroupsCleared={Ags}, PairingGroupsCleared={Pairings}",
            jobId, gamesDeleted, allAgIds.Count, teamCounts.Count);

        return Ok(new
        {
            gamesDeleted,
            agegroupsCleared = allAgIds.Count,
            pairingGroupsCleared = teamCounts.Count,
            profilesCleared = true
        });
    }
}
