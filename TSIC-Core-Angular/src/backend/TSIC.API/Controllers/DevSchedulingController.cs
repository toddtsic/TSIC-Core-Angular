using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos.Scheduling;
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
    private readonly IFieldRepository _fieldRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IAgeGroupRepository _agRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly ILogger<DevSchedulingController> _logger;

    public DevSchedulingController(
        IWebHostEnvironment env,
        IJobLookupService jobLookupService,
        IAutoBuildScheduleService autoBuildService,
        ITimeslotRepository tsRepo,
        IPairingsRepository pairingsRepo,
        IDivisionProfileRepository profileRepo,
        IFieldRepository fieldRepo,
        IJobRepository jobRepo,
        IAgeGroupRepository agRepo,
        ISchedulingContextResolver contextResolver,
        ILogger<DevSchedulingController> logger)
    {
        _env = env;
        _jobLookupService = jobLookupService;
        _autoBuildService = autoBuildService;
        _tsRepo = tsRepo;
        _pairingsRepo = pairingsRepo;
        _profileRepo = profileRepo;
        _fieldRepo = fieldRepo;
        _jobRepo = jobRepo;
        _agRepo = agRepo;
        _contextResolver = contextResolver;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/dev-scheduling/reset — Clear selected scheduling config for the current job,
    /// then optionally preconfigure from a prior year source.
    /// Deletes: games, strategy profiles, timeslot config, pairings, field assignments.
    /// </summary>
    [HttpPost("reset")]
    public async Task<ActionResult> Reset([FromBody] DevResetRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        // Resolve granular flags — legacy TimeslotConfig = both dates + field timeslots
        var clearDates = request.Dates || request.TimeslotConfig;
        var clearFieldTimeslots = request.FieldTimeslots || request.TimeslotConfig;

        _logger.LogWarning(
            "DEV RESET executing — Games={Games}, Profiles={Profiles}, Pairings={Pairings}, " +
            "Dates={Dates}, FieldTimeslots={FieldTimeslots}, Fields={Fields}. JobId={JobId}",
            request.Games, request.StrategyProfiles, request.Pairings,
            clearDates, clearFieldTimeslots, request.FieldAssignments, jobId);

        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId.Value, ct);

        // 1. Delete all games
        var gamesDeleted = 0;
        if (request.Games)
        {
            gamesDeleted = await _autoBuildService.UndoAsync(jobId.Value, ct);
        }

        // 2. Delete strategy profiles
        if (request.StrategyProfiles)
        {
            await _profileRepo.DeleteByJobIdAsync(jobId.Value, ct);
            await _profileRepo.SaveChangesAsync(ct);
        }

        // 3–4. Clear timeslot configuration (dates and/or field-timeslots independently)
        var agegroupsCleared = 0;
        if (clearDates || clearFieldTimeslots)
        {
            var agIds = new HashSet<Guid>();
            if (clearDates)
            {
                var ids = await _tsRepo.GetAgegroupIdsWithDatesAsync(leagueId, season, year, ct);
                foreach (var id in ids) agIds.Add(id);
            }
            if (clearFieldTimeslots)
            {
                var ids = await _tsRepo.GetAgegroupIdsWithFieldTimeslotsAsync(leagueId, season, year, ct);
                foreach (var id in ids) agIds.Add(id);
            }
            agegroupsCleared = agIds.Count;

            foreach (var agId in agIds)
            {
                if (clearDates)
                    await _tsRepo.DeleteAllDatesAsync(agId, season, year, ct);
                if (clearFieldTimeslots)
                    await _tsRepo.DeleteAllFieldTimeslotsAsync(agId, season, year, ct);
            }
            await _tsRepo.SaveChangesAsync(ct);
        }

        // 5. Delete pairings
        var pairingGroupsCleared = 0;
        if (request.Pairings)
        {
            var teamCounts = await _pairingsRepo.GetDistinctPoolSizesWithPairingsAsync(leagueId, season, ct);
            pairingGroupsCleared = teamCounts.Count;

            foreach (var tCnt in teamCounts)
            {
                await _pairingsRepo.DeleteAllAsync(leagueId, season, tCnt, ct);
            }
            await _pairingsRepo.SaveChangesAsync(ct);
        }

        // 6. Remove field-to-job assignments (FieldsLeagueSeason — NOT reference.Fields)
        var fieldsCleared = 0;
        if (request.FieldAssignments)
        {
            fieldsCleared = await _fieldRepo.RemoveAllFieldsFromLeagueSeasonAsync(leagueId, season, ct);
        }

        _logger.LogWarning(
            "DEV RESET complete. JobId={JobId}, GamesDeleted={Games}, " +
            "AgegroupsCleared={Ags}, PairingGroupsCleared={Pairings}, FieldsCleared={Fields}",
            jobId, gamesDeleted, agegroupsCleared, pairingGroupsCleared, fieldsCleared);

        // 7. Preconfigure from source if requested
        PreconfigureResult? preconfig = null;
        if (request.SourceJobId.HasValue)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "dev-reset";
            preconfig = await _autoBuildService.PreconfigureFromSourceAsync(
                jobId.Value, userId, request.SourceJobId.Value, ct);
        }

        return Ok(new
        {
            gamesDeleted,
            agegroupsCleared,
            pairingGroupsCleared,
            fieldsCleared,
            profilesCleared = request.StrategyProfiles,
            preconfig
        });
    }

    /// <summary>
    /// POST /api/dev-scheduling/preconfigure-from-source — Seed colors, dates, and field
    /// assignments from a prior year's tournament. Applies graduation-year agegroup mapping,
    /// date carry-forward (+1 year, DOW matched), and field constraint learning from
    /// historical game placements. Safe to call multiple times — only seeds data where
    /// none exists yet.
    /// </summary>
    [HttpPost("preconfigure-from-source")]
    public async Task<ActionResult> PreconfigureFromSource(
        [FromBody] PreconfigureRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var result = await _autoBuildService.PreconfigureFromSourceAsync(
            jobId.Value, userId, request.SourceJobId, ct);

        return Ok(result);
    }

    /// <summary>
    /// PUT /api/dev-scheduling/game-guarantee — Set event-level and/or per-agegroup game guarantee.
    /// </summary>
    [HttpPut("game-guarantee")]
    public async Task<ActionResult<SaveGameGuaranteeResponse>> SaveGameGuarantee(
        [FromBody] SaveGameGuaranteeRequest request, CancellationToken ct)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest(new { message = "Scheduling context required" });

        // 1. Update event-level default on Jobs
        await _jobRepo.UpdateGameGuaranteeAsync(jobId.Value, request.EventDefault, ct);

        // 2. Update per-agegroup overrides if provided
        var agUpdated = 0;
        if (request.AgegroupOverrides is { Count: > 0 })
        {
            var agDict = request.AgegroupOverrides
                .ToDictionary(
                    kvp => Guid.Parse(kvp.Key),
                    kvp => kvp.Value);
            agUpdated = await _agRepo.UpdateGameGuaranteesAsync(agDict, ct);
        }

        return Ok(new SaveGameGuaranteeResponse
        {
            EventDefault = request.EventDefault,
            AgegroupsUpdated = agUpdated
        });
    }
}
