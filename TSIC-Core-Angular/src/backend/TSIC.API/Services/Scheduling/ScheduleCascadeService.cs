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

        // Get current agegroup/division structure
        var divisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);

        // Event defaults (floor values)
        var eventGamePlacement = eventDefaults?.GamePlacement ?? "H";
        var eventBetweenRoundRows = eventDefaults?.BetweenRoundRows ?? (byte)1;

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

                // Resolve per-date effective waves:
                // Collect all dates from both agegroup and division wave tables
                var allDates = new HashSet<DateTime>();
                if (agWavesByDate != null)
                    foreach (var d in agWavesByDate.Keys) allDates.Add(d);
                if (divWavesByDate != null)
                    foreach (var d in divWavesByDate.Keys) allDates.Add(d);

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
                    EffectiveGamePlacement = divEffectivePlacement,
                    EffectiveBetweenRoundRows = divEffectiveBetweenRoundRows,
                    EffectiveWavesByDate = effectiveWaves
                });
            }

            agegroupDtos.Add(new AgegroupCascadeDto
            {
                AgegroupId = agId,
                AgegroupName = agGroup.Key.AgegroupName,
                GamePlacementOverride = agProfile?.GamePlacement,
                BetweenRoundRowsOverride = agProfile?.BetweenRoundRows,
                EffectiveGamePlacement = agEffectivePlacement,
                EffectiveBetweenRoundRows = agEffectiveBetweenRoundRows,
                WavesByDate = agWavesByDate ?? new Dictionary<DateTime, byte>(),
                Divisions = divisionDtos
            });
        }

        return new ScheduleCascadeSnapshot
        {
            EventDefaults = new EventScheduleDefaultsDto
            {
                GamePlacement = eventGamePlacement,
                BetweenRoundRows = eventBetweenRoundRows
            },
            Agegroups = agegroupDtos
        };
    }

    public async Task SaveEventDefaultsAsync(
        Guid jobId, string gamePlacement, byte betweenRoundRows,
        string userId, CancellationToken ct = default)
    {
        var entity = new EventScheduleDefaults
        {
            JobId = jobId,
            GamePlacement = gamePlacement,
            BetweenRoundRows = betweenRoundRows,
            LebUserId = userId
        };

        await _cascadeRepo.UpsertEventDefaultsAsync(entity, ct);
        await _cascadeRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved event defaults: JobId={JobId}, GamePlacement={GamePlacement}, BetweenRoundRows={BetweenRoundRows}",
            jobId, gamePlacement, betweenRoundRows);
    }

    public async Task SaveAgegroupOverrideAsync(
        Guid agegroupId, string? gamePlacement, byte? betweenRoundRows,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default)
    {
        // Profile: if both null, delete the override row
        if (gamePlacement == null && betweenRoundRows == null)
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
        Guid divisionId, string? gamePlacement, byte? betweenRoundRows,
        Dictionary<DateTime, byte>? wavesByDate,
        string userId, CancellationToken ct = default)
    {
        // Profile: if both null, delete the override row
        if (gamePlacement == null && betweenRoundRows == null)
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
}
