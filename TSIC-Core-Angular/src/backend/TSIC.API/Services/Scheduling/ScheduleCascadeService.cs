using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Resolves the 3-level scheduling cascade (Event → Agegroup → Division)
/// and provides CRUD for each level.
/// </summary>
public class ScheduleCascadeService : IScheduleCascadeService
{
    private readonly IScheduleCascadeRepository _cascadeRepo;
    private readonly IAutoBuildRepository _autoBuildRepo;
    private readonly ILogger<ScheduleCascadeService> _logger;

    public ScheduleCascadeService(
        IScheduleCascadeRepository cascadeRepo,
        IAutoBuildRepository autoBuildRepo,
        ILogger<ScheduleCascadeService> logger)
    {
        _cascadeRepo = cascadeRepo;
        _autoBuildRepo = autoBuildRepo;
        _logger = logger;
    }

    public async Task<ScheduleCascadeSnapshot> ResolveAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Load all cascade data (sequential — shared DbContext)
        var eventDefaults = await _cascadeRepo.GetEventDefaultsAsync(jobId, ct);
        var agProfiles = await _cascadeRepo.GetAgegroupProfilesAsync(jobId, ct);
        var divProfiles = await _cascadeRepo.GetDivisionProfilesAsync(jobId, ct);
        var agWaves = await _cascadeRepo.GetAgegroupWavesAsync(jobId, ct);
        var divWaves = await _cascadeRepo.GetDivisionWavesAsync(jobId, ct);
        var gameDatesByAgegroup = await _cascadeRepo.GetGameDatesByAgegroupAsync(jobId, ct);

        // Get current agegroup/division structure
        var divisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);

        // Event defaults (floor values)
        var eventGamePlacement = eventDefaults?.GamePlacement ?? "H";
        var eventBetweenRoundRows = eventDefaults?.BetweenRoundRows ?? (byte)1;
        var eventGameGuarantee = eventDefaults?.GameGuarantee ?? 0;

        // Index lookup tables
        var agProfileMap = agProfiles.ToDictionary(p => p.AgegroupId);
        var divProfileMap = divProfiles.ToDictionary(p => p.DivisionId);
        var agWaveMap = agWaves
            .GroupBy(w => w.AgegroupId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(w => w.GameDate, w => w.Wave));
        var divWaveMap = divWaves
            .GroupBy(w => w.DivisionId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(w => w.GameDate, w => w.Wave));

        // Group divisions by agegroup
        var agegroupGroups = divisions
            .GroupBy(d => new { d.AgegroupId, d.AgegroupName })
            .OrderBy(g => g.Key.AgegroupName);

        var agegroupDtos = new List<AgegroupCascadeDto>();

        foreach (var agGroup in agegroupGroups)
        {
            var agId = agGroup.Key.AgegroupId;
            agProfileMap.TryGetValue(agId, out var agProfile);
            agWaveMap.TryGetValue(agId, out var agWavesByDate);

            // Resolve agegroup-level effective values
            var agEffectivePlacement = agProfile?.GamePlacement ?? eventGamePlacement;
            var agEffectiveBetweenRoundRows = agProfile?.BetweenRoundRows ?? eventBetweenRoundRows;
            var agEffectiveGameGuarantee = agProfile?.GameGuarantee ?? eventGameGuarantee;

            var divisionDtos = new List<DivisionCascadeDto>();

            foreach (var div in agGroup.OrderBy(d => d.DivName))
            {
                divProfileMap.TryGetValue(div.DivId, out var divProfile);
                divWaveMap.TryGetValue(div.DivId, out var divWavesByDate);

                // Resolve division-level effective values
                var divEffectivePlacement = divProfile?.GamePlacement
                    ?? agEffectivePlacement;
                var divEffectiveBetweenRoundRows = divProfile?.BetweenRoundRows
                    ?? agEffectiveBetweenRoundRows;
                var divEffectiveGameGuarantee = divProfile?.GameGuarantee
                    ?? agEffectiveGameGuarantee;

                // Resolve per-date effective waves:
                // Collect dates from wave tables AND TLSD (game dates with no wave = wave 1)
                var allDates = new HashSet<DateTime>();
                if (agWavesByDate != null)
                    foreach (var d in agWavesByDate.Keys) allDates.Add(d);
                if (divWavesByDate != null)
                    foreach (var d in divWavesByDate.Keys) allDates.Add(d);
                if (gameDatesByAgegroup.TryGetValue(agId, out var tlsdDates))
                    foreach (var d in tlsdDates) allDates.Add(d);

                var effectiveWaves = new Dictionary<DateTime, byte>();
                foreach (var date in allDates)
                {
                    byte wave;
                    if (divWavesByDate != null && divWavesByDate.TryGetValue(date, out var divWave))
                        wave = divWave;
                    else if (agWavesByDate != null && agWavesByDate.TryGetValue(date, out var agWave))
                        wave = agWave;
                    else
                        wave = 1;
                    effectiveWaves[date] = wave;
                }

                divisionDtos.Add(new DivisionCascadeDto
                {
                    DivisionId = div.DivId,
                    DivisionName = div.DivName,
                    GamePlacementOverride = divProfile?.GamePlacement,
                    BetweenRoundRowsOverride = divProfile?.BetweenRoundRows,
                    GameGuaranteeOverride = divProfile?.GameGuarantee,
                    EffectiveGamePlacement = divEffectivePlacement,
                    EffectiveBetweenRoundRows = divEffectiveBetweenRoundRows,
                    EffectiveGameGuarantee = divEffectiveGameGuarantee,
                    EffectiveWavesByDate = effectiveWaves
                });
            }

            agegroupDtos.Add(new AgegroupCascadeDto
            {
                AgegroupId = agId,
                AgegroupName = agGroup.Key.AgegroupName,
                GamePlacementOverride = agProfile?.GamePlacement,
                BetweenRoundRowsOverride = agProfile?.BetweenRoundRows,
                GameGuaranteeOverride = agProfile?.GameGuarantee,
                EffectiveGamePlacement = agEffectivePlacement,
                EffectiveBetweenRoundRows = agEffectiveBetweenRoundRows,
                EffectiveGameGuarantee = agEffectiveGameGuarantee,
                WavesByDate = BuildAgWavesByDate(agWavesByDate, gameDatesByAgegroup.GetValueOrDefault(agId)),
                Divisions = divisionDtos
            });
        }

        return new ScheduleCascadeSnapshot
        {
            EventDefaults = new EventScheduleDefaultsDto
            {
                GamePlacement = eventGamePlacement,
                BetweenRoundRows = eventBetweenRoundRows,
                GameGuarantee = eventGameGuarantee
            },
            Agegroups = agegroupDtos
        };
    }

    public async Task SaveEventDefaultsAsync(
        Guid jobId, string gamePlacement, byte betweenRoundRows, int gameGuarantee,
        string userId, CancellationToken ct = default)
    {
        var entity = new EventScheduleDefaults
        {
            JobId = jobId,
            GamePlacement = gamePlacement,
            BetweenRoundRows = betweenRoundRows,
            GameGuarantee = gameGuarantee,
            LebUserId = userId
        };

        await _cascadeRepo.UpsertEventDefaultsAsync(entity, ct);
        await _cascadeRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved event defaults: JobId={JobId}, GamePlacement={GamePlacement}, BetweenRoundRows={BetweenRoundRows}, GameGuarantee={GameGuarantee}",
            jobId, gamePlacement, betweenRoundRows, gameGuarantee);
    }

    public async Task SaveAgegroupOverrideAsync(
        Guid agegroupId, string? gamePlacement, byte? betweenRoundRows, int? gameGuarantee,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default)
    {
        // Profile: if all null, delete the override row
        if (gamePlacement == null && betweenRoundRows == null && gameGuarantee == null)
        {
            await _cascadeRepo.DeleteAgegroupProfileAsync(agegroupId, ct);
        }
        else
        {
            var entity = new AgegroupScheduleProfile
            {
                AgegroupId = agegroupId,
                GamePlacement = gamePlacement,
                BetweenRoundRows = betweenRoundRows,
                GameGuarantee = gameGuarantee,
                LebUserId = userId
            };
            await _cascadeRepo.UpsertAgegroupProfileAsync(entity, ct);
        }

        // Waves: replace all for this agegroup
        if (wavesByDate != null)
        {
            var waves = wavesByDate
                .Select(kvp => new AgegroupWaveAssignment
                {
                    AgegroupId = agegroupId,
                    GameDate = kvp.Key,
                    Wave = kvp.Value,
                    LebUserId = userId
                })
                .ToList();
            await _cascadeRepo.UpsertAgegroupWavesAsync(agegroupId, waves, ct);
        }

        await _cascadeRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved agegroup override: AgegroupId={AgegroupId}, GamePlacement={GamePlacement}, " +
            "BetweenRoundRows={BetweenRoundRows}, WaveDates={WaveDateCount}",
            agegroupId, gamePlacement ?? "(inherit)", betweenRoundRows?.ToString() ?? "(inherit)",
            wavesByDate?.Count ?? 0);
    }

    public async Task SaveDivisionOverrideAsync(
        Guid divisionId, string? gamePlacement, byte? betweenRoundRows, int? gameGuarantee,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default)
    {
        // Profile: if all null, delete the override row
        if (gamePlacement == null && betweenRoundRows == null && gameGuarantee == null)
        {
            await _cascadeRepo.DeleteDivisionProfileAsync(divisionId, ct);
        }
        else
        {
            var entity = new DivisionScheduleProfile
            {
                DivisionId = divisionId,
                GamePlacement = gamePlacement,
                BetweenRoundRows = betweenRoundRows,
                GameGuarantee = gameGuarantee,
                LebUserId = userId
            };
            await _cascadeRepo.UpsertDivisionProfileAsync(entity, ct);
        }

        // Waves: replace all for this division
        if (wavesByDate != null)
        {
            var waves = wavesByDate
                .Select(kvp => new DivisionWaveAssignment
                {
                    DivisionId = divisionId,
                    GameDate = kvp.Key,
                    Wave = kvp.Value,
                    LebUserId = userId
                })
                .ToList();
            await _cascadeRepo.UpsertDivisionWavesAsync(divisionId, waves, ct);
        }

        await _cascadeRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved division override: DivisionId={DivisionId}, GamePlacement={GamePlacement}, " +
            "BetweenRoundRows={BetweenRoundRows}, WaveDates={WaveDateCount}",
            divisionId, gamePlacement ?? "(inherit)", betweenRoundRows?.ToString() ?? "(inherit)",
            wavesByDate?.Count ?? 0);
    }

    public async Task SaveBatchWavesAsync(
        Guid jobId, SaveBatchWavesRequest request, string userId,
        CancellationToken ct = default)
    {
        // Agegroup waves — sequential to avoid DbContext concurrency
        foreach (var (agIdStr, dateWaves) in request.AgegroupWaves)
        {
            if (!Guid.TryParse(agIdStr, out var agId)) continue;

            var waves = dateWaves
                .Select(kvp =>
                {
                    if (!DateTime.TryParse(kvp.Key, out var date)) return null;
                    return new AgegroupWaveAssignment
                    {
                        AgegroupId = agId,
                        GameDate = date.Date,
                        Wave = kvp.Value,
                        LebUserId = userId
                    };
                })
                .Where(w => w != null)
                .ToList()!;

            await _cascadeRepo.UpsertAgegroupWavesAsync(agId, waves!, ct);
        }

        // Division waves — sequential
        foreach (var (divIdStr, dateWaves) in request.DivisionWaves)
        {
            if (!Guid.TryParse(divIdStr, out var divId)) continue;

            var waves = dateWaves
                .Select(kvp =>
                {
                    if (!DateTime.TryParse(kvp.Key, out var date)) return null;
                    return new DivisionWaveAssignment
                    {
                        DivisionId = divId,
                        GameDate = date.Date,
                        Wave = kvp.Value,
                        LebUserId = userId
                    };
                })
                .Where(w => w != null)
                .ToList()!;

            await _cascadeRepo.UpsertDivisionWavesAsync(divId, waves!, ct);
        }

        await _cascadeRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Batch-saved wave assignments: {AgCount} agegroups, {DivCount} divisions",
            request.AgegroupWaves.Count, request.DivisionWaves.Count);
    }

    public async Task SeedDivisionWavesAsync(
        Guid jobId,
        Dictionary<Guid, int> divisionWaves,
        Dictionary<Guid, List<DateTime>> agegroupDates,
        string userId, CancellationToken ct = default)
    {
        // Get current division structure to map divisionId → agegroupId
        var divisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var divToAg = divisions.ToDictionary(d => d.DivId, d => d.AgegroupId);

        // Check which divisions already have wave assignments (skip those)
        var existingDivWaves = await _cascadeRepo.GetDivisionWavesAsync(jobId, ct);
        var divsWithWaves = existingDivWaves
            .Select(w => w.DivisionId)
            .ToHashSet();

        var seeded = 0;
        foreach (var (divId, wave) in divisionWaves)
        {
            if (divsWithWaves.Contains(divId)) continue;
            if (!divToAg.TryGetValue(divId, out var agId)) continue;
            if (!agegroupDates.TryGetValue(agId, out var dates) || dates.Count == 0) continue;

            var waveEntries = dates
                .Select(d => new DivisionWaveAssignment
                {
                    DivisionId = divId,
                    GameDate = d.Date,
                    Wave = (byte)Math.Clamp(wave, 1, 3),
                    LebUserId = userId
                })
                .ToList();

            await _cascadeRepo.UpsertDivisionWavesAsync(divId, waveEntries, ct);
            seeded++;
        }

        await _cascadeRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Seeded division waves from projection: JobId={JobId}, " +
            "DivisionsSeeded={Seeded}, DivisionsSkipped={Skipped}",
            jobId, seeded, divisionWaves.Count - seeded);
    }

    /// <summary>
    /// Merge explicit agegroup wave assignments with TLSD game dates.
    /// Dates in TLSD that have no wave row default to wave 1.
    /// </summary>
    private static Dictionary<DateTime, byte> BuildAgWavesByDate(
        Dictionary<DateTime, byte>? waveRows, HashSet<DateTime>? gameDates)
    {
        var result = new Dictionary<DateTime, byte>();

        if (waveRows != null)
            foreach (var kvp in waveRows)
                result[kvp.Key] = kvp.Value;

        if (gameDates != null)
            foreach (var d in gameDates)
                result.TryAdd(d, 1); // default wave 1 if no explicit assignment

        return result;
    }
}
