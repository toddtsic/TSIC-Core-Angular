using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Service for the Manage Timeslots scheduling tool.
/// Handles date/field CRUD, cloning, cartesian product creation, and capacity preview.
/// </summary>
public sealed class TimeslotService : ITimeslotService
{
    private readonly ITimeslotRepository _tsRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IJobLeagueRepository _jobLeagueRepo;
    private readonly IAgeGroupRepository _agRepo;
    private readonly IScheduleCascadeRepository _cascadeRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly ILogger<TimeslotService> _logger;

    /// <summary>Day-of-week cycling for clone-field-dow: Mon→Tue→…→Sun→Mon.</summary>
    private static readonly string[] DowCycle =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public TimeslotService(
        ITimeslotRepository tsRepo,
        IScheduleRepository scheduleRepo,
        IJobRepository jobRepo,
        IJobLeagueRepository jobLeagueRepo,
        IAgeGroupRepository agRepo,
        IScheduleCascadeRepository cascadeRepo,
        ISchedulingContextResolver contextResolver,
        ILogger<TimeslotService> logger)
    {
        _tsRepo = tsRepo;
        _scheduleRepo = scheduleRepo;
        _jobRepo = jobRepo;
        _jobLeagueRepo = jobLeagueRepo;
        _agRepo = agRepo;
        _cascadeRepo = cascadeRepo;
        _contextResolver = contextResolver;
        _logger = logger;
    }

    // ── Readiness ──

    public async Task<CanvasReadinessResponse> GetReadinessAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        var data = await _tsRepo.GetReadinessDataAsync(leagueId, season, year, ct);

        // Max round per agegroup from placed RR games (for round suggestions)
        var maxPairingRounds = await _scheduleRepo.GetMaxRoundByAgegroupAsync(leagueId, season, year, ct);

        // Count fields assigned to this league-season (FieldsLeagueSeason)
        var assignedFieldIds = await _tsRepo.GetAssignedFieldIdsAsync(leagueId, season, ct);

        // Event-level field summaries for the field config section
        var eventFields = await _tsRepo.GetEventFieldSummariesAsync(leagueId, season, ct);

        // Per-agegroup field IDs for the field config section
        var fieldIdsPerAg = await _tsRepo.GetFieldIdsPerAgegroupAsync(leagueId, season, year, ct);

        // Game guarantee from cascade: event default + per-agegroup overrides
        var cascadeEventDefaults = await _cascadeRepo.GetEventDefaultsAsync(jobId, ct);
        int? eventGameGuarantee = cascadeEventDefaults?.GameGuarantee;
        var cascadeAgProfiles = await _cascadeRepo.GetAgegroupProfilesAsync(jobId, ct);
        var agGuaranteeMap = cascadeAgProfiles.ToDictionary(p => p.AgegroupId, p => p.GameGuarantee);

        var agegroups = data.Select(kv =>
        {
            var d = kv.Value;
            // If only one distinct value exists, surface it; otherwise null = "mixed"
            int? gsi = d.DistinctGsi.Count == 1 ? d.DistinctGsi[0] : null;
            string? startTime = d.DistinctStartTimes.Count == 1 ? d.DistinctStartTimes[0] : null;
            int? maxGames = d.DistinctMaxGames.Count == 1 ? d.DistinctMaxGames[0] : null;

            // Total game slots = totalMaxGamesSum (per-DOW field capacity) × dates per DOW
            // DOW count comes from field timeslots (e.g. Saturday = 1 DOW)
            var totalSlots = 0;
            if (d.DaysOfWeek.Count > 0 && d.TotalMaxGamesSum > 0 && d.DateCount > 0)
            {
                var datesPerDow = (double)d.DateCount / d.DaysOfWeek.Count;
                totalSlots = (int)(d.TotalMaxGamesSum * datesPerDow);
            }

            // Build per game-day entries by joining actual dates with per-DOW field data
            var gameDays = BuildGameDays(d);

            return new AgegroupCanvasReadinessDto
            {
                AgegroupId = kv.Key,
                DateCount = d.DateCount,
                FieldCount = d.FieldCount,
                IsConfigured = d.DateCount > 0 && d.FieldCount > 0,
                DaysOfWeek = d.DaysOfWeek,
                GamestartInterval = gsi,
                StartTime = startTime,
                MaxGamesPerField = maxGames,
                TotalGameSlots = totalSlots,
                GameDays = gameDays,
                TotalRounds = d.RoundsPerDate.Values.Sum(),
                MaxPairingRound = maxPairingRounds.GetValueOrDefault(kv.Key, 0),
                GameGuarantee = agGuaranteeMap.GetValueOrDefault(kv.Key) ?? eventGameGuarantee,
                FieldIds = fieldIdsPerAg.GetValueOrDefault(kv.Key, [])
            };
        }).ToList();

        // Prior-year field defaults: look up sibling job from previous year
        PriorYearFieldDefaults? priorYearDefaults = null;
        Dictionary<string, int>? priorYearRounds = null;
        var priorJob = await _jobRepo.GetPriorYearJobAsync(jobId, ct);
        if (priorJob != null)
        {
            // Resolve the prior job's league context
            var priorLeagueId = await _jobLeagueRepo.GetPrimaryLeagueForJobAsync(priorJob.JobId, ct);
            if (priorLeagueId != null)
            {
                var priorSeasonYear = await _jobRepo.GetJobSeasonYearAsync(priorJob.JobId, ct);
                if (priorSeasonYear?.Season != null && priorSeasonYear.Year != null)
                {
                    var defaults = await _tsRepo.GetDominantFieldDefaultsAsync(
                        priorLeagueId.Value, priorSeasonYear.Season, priorSeasonYear.Year, ct);

                    if (defaults != null)
                    {
                        priorYearDefaults = new PriorYearFieldDefaults
                        {
                            PriorJobId = priorJob.JobId,
                            StartTime = defaults.StartTime,
                            GamestartInterval = defaults.GamestartInterval,
                            MaxGamesPerField = defaults.MaxGamesPerField,
                            PriorJobName = priorJob.JobName,
                            PriorYear = priorJob.Year
                        };
                    }

                    // Prior-year round counts per agegroup name (for round suggestions)
                    priorYearRounds = await _tsRepo.GetRoundCountsByAgegroupNameAsync(
                        priorLeagueId.Value, priorSeasonYear.Season, priorSeasonYear.Year, ct);
                    if (priorYearRounds.Count == 0) priorYearRounds = null;
                }
            }
        }

        return new CanvasReadinessResponse
        {
            Agegroups = agegroups,
            AssignedFieldCount = assignedFieldIds.Count,
            PriorYearDefaults = priorYearDefaults,
            PriorYearRounds = priorYearRounds,
            EventFields = eventFields
        };
    }

    /// <summary>
    /// Build per game-day DTOs by joining actual calendar dates with per-DOW field schedules.
    /// Each date gets its DOW's field count, start time, calculated end time, and GSI.
    /// </summary>
    private static List<GameDayDto> BuildGameDays(AgegroupReadinessData d)
    {
        if (d.Dates.Count == 0 || d.PerDowFields.Count == 0)
            return [];

        // Build DOW lookup: full day name → DowFieldData
        var dowLookup = d.PerDowFields.ToDictionary(f => f.Dow, f => f, StringComparer.OrdinalIgnoreCase);

        // DOW abbreviation map
        var dowAbbrev = new Dictionary<DayOfWeek, string>
        {
            [DayOfWeek.Sunday] = "Sun",
            [DayOfWeek.Monday] = "Mon",
            [DayOfWeek.Tuesday] = "Tue",
            [DayOfWeek.Wednesday] = "Wed",
            [DayOfWeek.Thursday] = "Thu",
            [DayOfWeek.Friday] = "Fri",
            [DayOfWeek.Saturday] = "Sat"
        };

        var result = new List<GameDayDto>();
        var dayNumber = 1;

        foreach (var date in d.Dates.OrderBy(dt => dt))
        {
            var dayName = date.DayOfWeek.ToString(); // "Saturday", "Sunday", etc.

            if (!dowLookup.TryGetValue(dayName, out var dowFields))
            {
                // Date exists but no field config for this DOW — skip
                continue;
            }

            // Start time: pick the earliest if multiple
            var earliestStart = dowFields.StartTimes
                .OrderBy(t => t)
                .FirstOrDefault() ?? "";

            // Max games per field: use the most common (or first if all same)
            var maxGames = dowFields.MaxGamesValues.Count == 1
                ? dowFields.MaxGamesValues[0]
                : dowFields.MaxGamesValues.Max();

            // GSI: use the most common (or first if all same)
            var gsiVal = dowFields.GsiValues.Count == 1
                ? dowFields.GsiValues[0]
                : dowFields.GsiValues.Min();

            // Calculate end time: parse start time, add (maxGames × GSI) minutes
            var endTime = CalculateEndTime(earliestStart, maxGames, gsiVal);

            result.Add(new GameDayDto
            {
                DayNumber = dayNumber,
                Date = date,
                Dow = dowAbbrev.GetValueOrDefault(date.DayOfWeek, dayName[..3]),
                FieldCount = dowFields.FieldCount,
                StartTime = earliestStart,
                EndTime = endTime,
                Gsi = gsiVal,
                TotalSlots = dowFields.TotalMaxGamesSum,
                RoundCount = d.RoundsPerDate.GetValueOrDefault(date.Date, 0)
            });

            dayNumber++;
        }

        return result;
    }

    /// <summary>
    /// Calculate end time by parsing "HH:mm AM/PM" start time and adding (maxGames × gsi) minutes.
    /// Returns formatted end time string, or empty string if parsing fails.
    /// </summary>
    private static string CalculateEndTime(string startTime, int maxGames, int gsiMinutes)
    {
        if (string.IsNullOrEmpty(startTime))
            return "";

        // Try multiple time formats
        string[] formats = ["h:mm tt", "hh:mm tt", "H:mm", "HH:mm"];
        if (TimeSpan.TryParse(startTime, out var ts))
        {
            var end = ts.Add(TimeSpan.FromMinutes(maxGames * gsiMinutes));
            var endDt = DateTime.Today.Add(end);
            return endDt.ToString("h:mm tt");
        }

        if (DateTime.TryParseExact(startTime, formats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed))
        {
            var end = parsed.AddMinutes(maxGames * gsiMinutes);
            return end.ToString("h:mm tt");
        }

        return "";
    }

    // ── Configuration ──

    public async Task<TimeslotConfigurationResponse> GetConfigurationAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var dates = await _tsRepo.GetDatesAsync(agegroupId, season, year, ct);
        var fields = await _tsRepo.GetFieldTimeslotsAsync(agegroupId, season, year, ct);

        return new TimeslotConfigurationResponse
        {
            Dates = dates,
            Fields = fields
        };
    }

    public async Task<List<CapacityPreviewDto>> GetCapacityPreviewAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        var fields = await _tsRepo.GetFieldTimeslotsAsync(agegroupId, season, year, ct);

        // Get team count for this agegroup (approximate from division with most teams)
        var divIds = await _tsRepo.GetActiveDivisionIdsAsync(agegroupId, jobId, ct);
        var maxTeamCount = 0;
        foreach (var divId in divIds)
        {
            var tc = await _tsRepo.GetPairingCountAsync(leagueId, season, maxTeamCount, ct);
            if (tc > maxTeamCount) maxTeamCount = tc;
        }

        // Group field timeslots by DOW
        var byDow = fields.GroupBy(f => f.Dow);
        var result = new List<CapacityPreviewDto>();

        foreach (var group in byDow)
        {
            var totalSlots = group.Sum(f => f.MaxGamesPerField);
            var fieldCount = group.Select(f => f.FieldId).Distinct().Count();
            var gamesNeeded = maxTeamCount > 0 ? (int)Math.Ceiling(maxTeamCount / 2.0) : 0;

            result.Add(new CapacityPreviewDto
            {
                Dow = group.Key,
                FieldCount = fieldCount,
                TotalGameSlots = totalSlots,
                GamesNeeded = gamesNeeded,
                IsSufficient = totalSlots >= gamesNeeded
            });
        }

        return result;
    }

    // ── Dates CRUD ──

    public async Task<TimeslotDateDto> AddDateAsync(
        Guid jobId, string userId, AddTimeslotDateRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var date = new TimeslotsLeagueSeasonDates
        {
            AgegroupId = request.AgegroupId,
            GDate = request.GDate,
            Rnd = request.Rnd,
            DivId = request.DivId,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _tsRepo.AddDate(date);
        await _tsRepo.SaveChangesAsync(ct);
        return MapDateDto(date);
    }

    public async Task EditDateAsync(
        string userId, EditTimeslotDateRequest request, CancellationToken ct = default)
    {
        var date = await _tsRepo.GetDateByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Timeslot date {request.Ai} not found.");

        date.GDate = request.GDate;
        date.Rnd = request.Rnd;
        date.LebUserId = userId;
        date.Modified = DateTime.UtcNow;

        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteDateAsync(int ai, CancellationToken ct = default)
    {
        var date = await _tsRepo.GetDateByIdAsync(ai, ct)
            ?? throw new KeyNotFoundException($"Timeslot date {ai} not found.");
        _tsRepo.RemoveDate(date);
        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteAllDatesAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        await _tsRepo.DeleteAllDatesAsync(agegroupId, season, year, ct);
        await _tsRepo.SaveChangesAsync(ct);
    }

    // ── Cascade date operations ──

    public async Task<CascadeDateChangeResponse> CascadeEditDateAsync(
        Guid jobId, string userId, CascadeDateChangeRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // ① Duplicate check
        if (await _tsRepo.DateExistsAsync(leagueId, request.NewDate, season, year, ct))
            throw new InvalidOperationException(
                $"A game date already exists for {request.NewDate:yyyy-MM-dd}.");

        // ② Update TimeslotsLeagueSeasonDates rows (tracked)
        var dateRows = await _tsRepo.GetDatesByDateTrackedAsync(
            leagueId, request.OldDate, season, year, ct);

        var now = DateTime.UtcNow;
        foreach (var row in dateRows)
        {
            row.GDate = request.NewDate;
            row.LebUserId = userId;
            row.Modified = now;
        }

        // ③ Migrate agegroup wave assignments (composite PK includes GameDate — must delete+insert)
        var agWaves = await _cascadeRepo.GetAgegroupWavesByDateAsync(jobId, request.OldDate, ct);
        var agWavesMigrated = agWaves.Count;
        if (agWaves.Count > 0)
        {
            // Snapshot values before deletion (tracked entities will be removed)
            var snapshots = agWaves.Select(w => (w.AgegroupId, w.Wave)).ToList();
            await _cascadeRepo.DeleteAgegroupWavesByDateAsync(jobId, request.OldDate, ct);

            // Insert new rows with updated GameDate
            foreach (var (agId, wave) in snapshots)
            {
                _cascadeRepo.AddAgegroupWave(new AgegroupWaveAssignment
                {
                    AgegroupId = agId,
                    GameDate = request.NewDate,
                    Wave = wave,
                    LebUserId = userId,
                    Modified = now
                });
            }
        }

        // ④ Migrate division wave assignments
        var divWaves = await _cascadeRepo.GetDivisionWavesByDateAsync(jobId, request.OldDate, ct);
        var divWavesMigrated = divWaves.Count;
        if (divWaves.Count > 0)
        {
            var snapshots = divWaves.Select(w => (w.DivisionId, w.Wave)).ToList();
            await _cascadeRepo.DeleteDivisionWavesByDateAsync(jobId, request.OldDate, ct);

            foreach (var (divId, wave) in snapshots)
            {
                _cascadeRepo.AddDivisionWave(new DivisionWaveAssignment
                {
                    DivisionId = divId,
                    GameDate = request.NewDate,
                    Wave = wave,
                    LebUserId = userId,
                    Modified = now
                });
            }
        }

        // ⑤ Update scheduled games
        var gamesUpdated = await _scheduleRepo.UpdateGameDatesAsync(
            jobId, request.OldDate, request.NewDate, ct);

        // ⑥ Handle DOW change — stage field timeslots for new DOW if needed
        var oldDow = request.OldDate.DayOfWeek.ToString();
        var newDow = request.NewDate.DayOfWeek.ToString();
        var dowChanged = !string.Equals(oldDow, newDow, StringComparison.OrdinalIgnoreCase);
        var fieldTimeslotsCreated = 0;

        if (dowChanged)
        {
            var affectedAgIds = dateRows.Select(r => r.AgegroupId).Distinct().ToList();
            var assignedFieldIds = await _tsRepo.GetAssignedFieldIdsAsync(leagueId, season, ct);

            foreach (var agId in affectedAgIds)
            {
                // Check if field timeslots exist for the new DOW
                var existingNewDow = await _tsRepo.GetFieldTimeslotsByFilterAsync(
                    agId, season, year, dow: newDow, ct: ct);

                if (existingNewDow.Count == 0 && assignedFieldIds.Count > 0)
                {
                    // Try to copy from old DOW as template
                    var oldDowTimeslots = await _tsRepo.GetFieldTimeslotsByFilterAsync(
                        agId, season, year, dow: oldDow, ct: ct);

                    if (oldDowTimeslots.Count > 0)
                    {
                        // Clone structure from old DOW
                        var newTimeslots = oldDowTimeslots.Select(t => new TimeslotsLeagueSeasonFields
                        {
                            AgegroupId = agId,
                            FieldId = t.FieldId,
                            DivId = t.DivId,
                            StartTime = t.StartTime,
                            GamestartInterval = t.GamestartInterval,
                            MaxGamesPerField = t.MaxGamesPerField,
                            Dow = newDow,
                            Season = season,
                            Year = year,
                            LebUserId = userId,
                            Modified = now
                        }).ToList();

                        await _tsRepo.AddFieldTimeslotsRangeAsync(newTimeslots, ct);
                        fieldTimeslotsCreated += newTimeslots.Count;
                    }
                    else
                    {
                        // Fallback: use dominant defaults + cartesian product
                        var defaults = await _tsRepo.GetDominantFieldDefaultsAsync(
                            leagueId, season, year, ct);
                        var divIds = await _tsRepo.GetActiveDivisionIdsAsync(agId, jobId, ct);

                        var newTimeslots = new List<TimeslotsLeagueSeasonFields>();
                        foreach (var fId in assignedFieldIds)
                        {
                            foreach (var dId in divIds)
                            {
                                newTimeslots.Add(new TimeslotsLeagueSeasonFields
                                {
                                    AgegroupId = agId,
                                    FieldId = fId,
                                    DivId = dId,
                                    StartTime = defaults?.StartTime ?? "08:00 AM",
                                    GamestartInterval = defaults?.GamestartInterval ?? 60,
                                    MaxGamesPerField = defaults?.MaxGamesPerField ?? 5,
                                    Dow = newDow,
                                    Season = season,
                                    Year = year,
                                    LebUserId = userId,
                                    Modified = now
                                });
                            }
                        }

                        if (newTimeslots.Count > 0)
                        {
                            await _tsRepo.AddFieldTimeslotsRangeAsync(newTimeslots, ct);
                            fieldTimeslotsCreated += newTimeslots.Count;
                        }
                    }
                }
            }
        }

        await _tsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Cascade date edit: {OldDate:yyyy-MM-dd} → {NewDate:yyyy-MM-dd} — " +
            "{DateRows} date rows, {Waves} waves migrated, {Games} games updated, " +
            "{Fields} field timeslots created, DOW changed: {DowChanged}",
            request.OldDate, request.NewDate,
            dateRows.Count, agWavesMigrated + divWavesMigrated,
            gamesUpdated, fieldTimeslotsCreated, dowChanged);

        return new CascadeDateChangeResponse
        {
            DateRowsUpdated = dateRows.Count,
            WavesMigrated = agWavesMigrated + divWavesMigrated,
            GamesUpdated = gamesUpdated,
            FieldTimeslotsCreated = fieldTimeslotsCreated,
            DowChanged = dowChanged
        };
    }

    public async Task<CascadeDateDeleteResponse> CascadeDeleteDateAsync(
        Guid jobId, CascadeDateDeleteRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // ① Get and delete TimeslotsLeagueSeasonDates rows
        var dateRows = await _tsRepo.GetDatesByDateTrackedAsync(
            leagueId, request.Date, season, year, ct);
        var dateRowCount = dateRows.Count;

        if (dateRows.Count > 0)
        {
            foreach (var row in dateRows)
                _tsRepo.RemoveDate(row);
        }

        // ② Delete agegroup wave assignments for this date
        await _cascadeRepo.DeleteAgegroupWavesByDateAsync(jobId, request.Date, ct);

        // ③ Delete division wave assignments for this date
        await _cascadeRepo.DeleteDivisionWavesByDateAsync(jobId, request.Date, ct);

        // ④ Delete scheduled games for this date (with cascade to DeviceGids/BracketSeeds)
        var gamesDeleted = await _scheduleRepo.DeleteGamesByDateAsync(jobId, request.Date, ct);

        // Field timeslots are DOW-scoped — do NOT delete (other dates may share the DOW)

        await _tsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Cascade date delete: {Date:yyyy-MM-dd} — {DateRows} date rows, {Games} games deleted",
            request.Date, dateRowCount, gamesDeleted);

        return new CascadeDateDeleteResponse
        {
            DateRowsDeleted = dateRowCount,
            WavesDeleted = 0, // Wave delete methods don't return counts currently
            GamesDeleted = gamesDeleted
        };
    }

    // ── Date cloning ──

    public async Task<TimeslotDateDto> CloneDateRecordAsync(
        string userId, CloneDateRecordRequest request, CancellationToken ct = default)
    {
        var source = await _tsRepo.GetDateByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Timeslot date {request.Ai} not found.");

        var clone = new TimeslotsLeagueSeasonDates
        {
            AgegroupId = source.AgegroupId,
            DivId = source.DivId,
            Season = source.Season,
            Year = source.Year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        switch (request.CloneType.ToLowerInvariant())
        {
            case "day":
                clone.GDate = source.GDate.AddDays(1);
                clone.Rnd = source.Rnd + 1;
                break;
            case "week":
                clone.GDate = source.GDate.AddDays(7);
                clone.Rnd = source.Rnd + 1;
                break;
            case "round":
                clone.GDate = source.GDate;
                clone.Rnd = source.Rnd + 1;
                break;
            default:
                throw new ArgumentException($"Invalid clone type: {request.CloneType}");
        }

        _tsRepo.AddDate(clone);
        await _tsRepo.SaveChangesAsync(ct);
        return MapDateDto(clone);
    }

    // ── Field timeslots CRUD ──

    public async Task<List<TimeslotFieldDto>> AddFieldTimeslotAsync(
        Guid jobId, string userId, AddTimeslotFieldRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Determine field IDs (single or all assigned)
        var fieldIds = request.FieldId.HasValue
            ? [request.FieldId.Value]
            : await _tsRepo.GetAssignedFieldIdsAsync(leagueId, season, ct);

        if (fieldIds.Count == 0)
        {
            _logger.LogWarning(
                "No fields assigned to league-season for agegroup {AgId}. " +
                "Use Manage Fields to assign fields before creating field schedules.",
                request.AgegroupId);
            throw new InvalidOperationException(
                "No fields are assigned to this event. Use Manage Fields to assign fields first.");
        }

        // Determine division IDs (single or all active)
        var divIds = request.DivId.HasValue
            ? [request.DivId.Value]
            : await _tsRepo.GetActiveDivisionIdsAsync(request.AgegroupId, jobId, ct);

        // Cartesian product: Fields × Divisions
        var newTimeslots = new List<TimeslotsLeagueSeasonFields>();
        foreach (var fId in fieldIds)
        {
            foreach (var dId in divIds)
            {
                newTimeslots.Add(new TimeslotsLeagueSeasonFields
                {
                    AgegroupId = request.AgegroupId,
                    FieldId = fId,
                    DivId = dId,
                    StartTime = request.StartTime,
                    GamestartInterval = request.GamestartInterval,
                    MaxGamesPerField = request.MaxGamesPerField,
                    Dow = request.Dow,
                    Season = season,
                    Year = year,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                });
            }
        }

        if (newTimeslots.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(newTimeslots, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Added {Count} field timeslots for agegroup {AgId}", newTimeslots.Count, request.AgegroupId);

        // Reload as projected DTOs
        return await _tsRepo.GetFieldTimeslotsAsync(request.AgegroupId, season, year, ct);
    }

    public async Task EditFieldTimeslotAsync(
        string userId, EditTimeslotFieldRequest request, CancellationToken ct = default)
    {
        var ts = await _tsRepo.GetFieldTimeslotByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Field timeslot {request.Ai} not found.");

        ts.StartTime = request.StartTime;
        ts.GamestartInterval = request.GamestartInterval;
        ts.MaxGamesPerField = request.MaxGamesPerField;
        ts.Dow = request.Dow;
        if (request.FieldId.HasValue) ts.FieldId = request.FieldId.Value;
        if (request.DivId.HasValue) ts.DivId = request.DivId;
        ts.LebUserId = userId;
        ts.Modified = DateTime.UtcNow;

        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteFieldTimeslotAsync(int ai, CancellationToken ct = default)
    {
        var ts = await _tsRepo.GetFieldTimeslotByIdAsync(ai, ct)
            ?? throw new KeyNotFoundException($"Field timeslot {ai} not found.");
        _tsRepo.RemoveFieldTimeslot(ts);
        await _tsRepo.SaveChangesAsync(ct);
    }

    public async Task DeleteAllFieldTimeslotsAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        await _tsRepo.DeleteAllFieldTimeslotsAsync(agegroupId, season, year, ct);
        await _tsRepo.SaveChangesAsync(ct);
    }

    // ── Cloning: Dates agegroup→agegroup ──

    public async Task CloneDatesAsync(
        Guid jobId, string userId, CloneDatesRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Delete existing target dates first
        await _tsRepo.DeleteAllDatesAsync(request.TargetAgegroupId, season, year, ct);

        var sourceDates = await _tsRepo.GetDatesByAgegroupAsync(request.SourceAgegroupId, season, year, ct);

        foreach (var src in sourceDates)
        {
            _tsRepo.AddDate(new TimeslotsLeagueSeasonDates
            {
                AgegroupId = request.TargetAgegroupId,
                GDate = src.GDate,
                Rnd = src.Rnd,
                DivId = src.DivId,
                Season = season,
                Year = year,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            });
        }

        await _tsRepo.SaveChangesAsync(ct);
        _logger.LogInformation("Cloned {Count} dates from AG {Src} to AG {Tgt}",
            sourceDates.Count, request.SourceAgegroupId, request.TargetAgegroupId);
    }

    // ── Cloning: Fields agegroup→agegroup ──

    public async Task CloneFieldsAsync(
        Guid jobId, string userId, CloneFieldsRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceFields = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.SourceAgegroupId, season, year, ct: ct);

        var clones = sourceFields.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.TargetAgegroupId,
            FieldId = src.FieldId,
            DivId = src.DivId,
            StartTime = src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = src.Dow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Cloned {Count} field timeslots from AG {Src} to AG {Tgt}",
            clones.Count, request.SourceAgegroupId, request.TargetAgegroupId);
    }

    // ── Cloning: by field within agegroup ──

    public async Task CloneByFieldAsync(
        Guid jobId, string userId, CloneByFieldRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceTimeslots = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.AgegroupId, season, year, fieldId: request.SourceFieldId, ct: ct);

        var clones = sourceTimeslots.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.AgegroupId,
            FieldId = request.TargetFieldId,
            DivId = src.DivId,
            StartTime = src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = src.Dow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }
    }

    // ── Cloning: by division within agegroup ──

    public async Task CloneByDivisionAsync(
        Guid jobId, string userId, CloneByDivisionRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceTimeslots = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.AgegroupId, season, year, divId: request.SourceDivId, ct: ct);

        var clones = sourceTimeslots.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.AgegroupId,
            FieldId = src.FieldId,
            DivId = request.TargetDivId,
            StartTime = src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = src.Dow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }
    }

    // ── Cloning: by day-of-week within agegroup ──

    public async Task CloneByDowAsync(
        Guid jobId, string userId, CloneByDowRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var sourceTimeslots = await _tsRepo.GetFieldTimeslotsByFilterAsync(
            request.AgegroupId, season, year, dow: request.SourceDow, ct: ct);

        var clones = sourceTimeslots.Select(src => new TimeslotsLeagueSeasonFields
        {
            AgegroupId = request.AgegroupId,
            FieldId = src.FieldId,
            DivId = src.DivId,
            StartTime = request.NewStartTime ?? src.StartTime,
            GamestartInterval = src.GamestartInterval,
            MaxGamesPerField = src.MaxGamesPerField,
            Dow = request.TargetDow,
            Season = season,
            Year = year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        }).ToList();

        if (clones.Count > 0)
        {
            await _tsRepo.AddFieldTimeslotsRangeAsync(clones, ct);
            await _tsRepo.SaveChangesAsync(ct);
        }
    }

    // ── Cloning: single field record to next DOW ──

    public async Task<TimeslotFieldDto> CloneFieldDowAsync(
        string userId, CloneFieldDowRequest request, CancellationToken ct = default)
    {
        var source = await _tsRepo.GetFieldTimeslotByIdAsync(request.Ai, ct)
            ?? throw new KeyNotFoundException($"Field timeslot {request.Ai} not found.");

        var nextDow = GetNextDow(source.Dow);

        var clone = new TimeslotsLeagueSeasonFields
        {
            AgegroupId = source.AgegroupId,
            FieldId = source.FieldId,
            DivId = source.DivId,
            StartTime = source.StartTime,
            GamestartInterval = source.GamestartInterval,
            MaxGamesPerField = source.MaxGamesPerField,
            Dow = nextDow,
            Season = source.Season,
            Year = source.Year,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _tsRepo.AddFieldTimeslot(clone);
        await _tsRepo.SaveChangesAsync(ct);

        // Reload with navigation properties
        var reloaded = await _tsRepo.GetFieldTimeslotByIdAsync(clone.Ai, ct);
        return MapFieldDto(reloaded ?? clone);
    }

    // ── Bulk operations ──

    public async Task<BulkDateAssignResponse> BulkAssignDateAsync(
        Guid jobId, string userId, BulkDateAssignRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Pre-fetch assigned fields (shared across all agegroups)
        var assignedFieldIds = await _tsRepo.GetAssignedFieldIdsAsync(leagueId, season, ct);

        // Determine DOW from the date (full name: "Saturday", "Sunday", etc.)
        var dow = request.GDate.DayOfWeek.ToString();

        var results = new List<BulkDateAssignResult>();

        // Resolve entries: prefer per-agegroup Entries, fall back to legacy AgegroupIds
        var entries = request.Entries?.Count > 0
            ? request.Entries
            : (request.AgegroupIds ?? []).Select(id =>
                new BulkDateAgegroupEntry { AgegroupId = id }).ToList();

        // Process each agegroup sequentially (DbContext is not thread-safe)
        foreach (var entry in entries)
        {
            var agegroupId = entry.AgegroupId;
            var dateCreated = false;
            var roundsCreated = 0;
            var fieldTimeslotsCreated = 0;

            // Calculate wave-based start time offset
            var startTime = CalculateWaveStartTime(
                request.StartTime, entry.Wave, request.GamestartInterval, request.MaxGamesPerField);

            // ① Start-round marker: one TLSD row per AG-date with Rnd = starting round.
            // For a new date assignment, default Rnd = 1. The Rounds Per Day tab
            // handles multi-day break points (e.g., Friday=1, Saturday=3).
            var existingDates = await _tsRepo.GetDatesByAgegroupAsync(agegroupId, season, year, ct);
            var alreadyHasDate = existingDates.Any(d => d.GDate.Date == request.GDate.Date && d.DivId == null);

            if (!alreadyHasDate)
            {
                _tsRepo.AddDate(new TimeslotsLeagueSeasonDates
                {
                    AgegroupId = agegroupId,
                    GDate = request.GDate,
                    Rnd = 1,
                    Season = season,
                    Year = year,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                });

                dateCreated = true;
                roundsCreated = 1;
            }

            // ② Check if field timeslots exist for this DOW
            var existingFields = await _tsRepo.GetFieldTimeslotsByFilterAsync(
                agegroupId, season, year, dow: dow, ct: ct);

            if (existingFields.Count == 0 && assignedFieldIds.Count > 0)
            {
                // Get active division IDs for this agegroup
                var divIds = await _tsRepo.GetActiveDivisionIdsAsync(agegroupId, jobId, ct);

                // Cartesian product: fields × divisions
                var newTimeslots = new List<TimeslotsLeagueSeasonFields>();
                foreach (var fId in assignedFieldIds)
                {
                    foreach (var dId in divIds)
                    {
                        newTimeslots.Add(new TimeslotsLeagueSeasonFields
                        {
                            AgegroupId = agegroupId,
                            FieldId = fId,
                            DivId = dId,
                            StartTime = startTime,
                            GamestartInterval = request.GamestartInterval,
                            MaxGamesPerField = request.MaxGamesPerField,
                            Dow = dow,
                            Season = season,
                            Year = year,
                            LebUserId = userId,
                            Modified = DateTime.UtcNow
                        });
                    }
                }

                if (newTimeslots.Count > 0)
                {
                    await _tsRepo.AddFieldTimeslotsRangeAsync(newTimeslots, ct);
                    fieldTimeslotsCreated = newTimeslots.Count;
                }
            }

            results.Add(new BulkDateAssignResult
            {
                AgegroupId = agegroupId,
                DateCreated = dateCreated,
                RoundsCreated = roundsCreated,
                FieldTimeslotsCreated = fieldTimeslotsCreated
            });
        }

        // ③ Process removals — agegroups unchecked from this existing date
        var removedCount = 0;
        if (request.RemovedAgegroupIds is { Count: > 0 })
        {
            foreach (var agId in request.RemovedAgegroupIds)
            {
                await _tsRepo.DeleteDatesByDateAsync(agId, request.GDate, season, year, ct);
                removedCount++;
            }
        }

        // Single save for all agegroups (additions + removals)
        await _tsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Bulk date assign: {Date:yyyy-MM-dd} → {Count} agegroups, {DatesCreated} dates, {FieldsCreated} field timeslots, {Removed} removed",
            request.GDate,
            entries.Count,
            results.Count(r => r.DateCreated),
            results.Sum(r => r.FieldTimeslotsCreated),
            removedCount);

        return new BulkDateAssignResponse { Results = results };
    }

    // ── Field config update ──

    public async Task<UpdateFieldConfigResponse> UpdateFieldConfigAsync(
        Guid jobId, string userId, UpdateFieldConfigRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Fetch all TRACKED field timeslot entities for this league-season
        var allRows = await _tsRepo.GetAllFieldTimeslotsForUpdateAsync(leagueId, season, year, ct);

        if (allRows.Count == 0)
            return new UpdateFieldConfigResponse { RowsUpdated = 0 };

        int updatedCount;

        if (request.Entries is { Count: > 0 })
        {
            // Per-AG mode: client sends pre-calculated values per agegroup
            updatedCount = ApplyPerAgConfig(allRows, request.Entries, userId);
        }
        else
        {
            // Uniform mode: infer waves from current start time offsets, recalculate
            updatedCount = ApplyUniformConfig(allRows, request, userId);
        }

        // Per-AG-per-DOW overrides: second pass that refines individual (agegroup, DOW) groups.
        // Applied AFTER uniform/per-AG to allow the matrix to override specific cells.
        if (request.AgDowOverrides is { Count: > 0 })
        {
            updatedCount += ApplyAgDowOverrides(allRows, request.AgDowOverrides, userId);
        }

        if (updatedCount > 0)
            await _tsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated field config: {Count} rows for league-season {Season}/{Year}",
            updatedCount, season, year);

        return new UpdateFieldConfigResponse { RowsUpdated = updatedCount };
    }

    /// <summary>Per-AG mode: apply values from entries directly to matching rows.</summary>
    private static int ApplyPerAgConfig(
        List<TimeslotsLeagueSeasonFields> allRows,
        List<FieldConfigAgegroupEntry> entries,
        string userId)
    {
        var entryMap = entries.ToDictionary(e => e.AgegroupId);
        var updated = 0;

        foreach (var row in allRows)
        {
            if (!entryMap.TryGetValue(row.AgegroupId, out var entry))
                continue;

            var changed = false;

            if (entry.StartTime != null && row.StartTime != entry.StartTime)
            {
                row.StartTime = entry.StartTime;
                changed = true;
            }

            if (entry.GamestartInterval.HasValue && row.GamestartInterval != entry.GamestartInterval.Value)
            {
                row.GamestartInterval = entry.GamestartInterval.Value;
                changed = true;
            }

            if (entry.MaxGamesPerField.HasValue && row.MaxGamesPerField != entry.MaxGamesPerField.Value)
            {
                row.MaxGamesPerField = entry.MaxGamesPerField.Value;
                changed = true;
            }

            if (changed)
            {
                row.LebUserId = userId;
                row.Modified = DateTime.UtcNow;
                updated++;
            }
        }

        return updated;
    }

    /// <summary>
    /// Uniform mode: infer wave per (agegroup, dow) group from current start time offsets,
    /// then recalculate start times with new parameters while preserving wave assignments.
    /// </summary>
    private static int ApplyUniformConfig(
        List<TimeslotsLeagueSeasonFields> allRows,
        UpdateFieldConfigRequest request,
        string userId)
    {
        // Nothing to change if all fields are null
        if (request.BaseStartTime == null && !request.GamestartInterval.HasValue && !request.MaxGamesPerField.HasValue)
            return 0;

        // Group by (AgegroupId, Dow) — each group shares the same time config
        var groups = allRows
            .GroupBy(r => (r.AgegroupId, r.Dow))
            .ToList();

        // Step 1: Find current dominant GSI and MaxGames (most common values across all rows)
        var currentGsi = allRows
            .GroupBy(r => r.GamestartInterval)
            .OrderByDescending(g => g.Count())
            .First().Key;

        var currentMaxGames = allRows
            .GroupBy(r => r.MaxGamesPerField)
            .OrderByDescending(g => g.Count())
            .First().Key;

        // Step 2: Parse each group's dominant start time to minutes-from-midnight
        var currentBaseMinutes = int.MaxValue;
        var groupStartMinutes = new Dictionary<(Guid, string), int>();

        foreach (var g in groups)
        {
            // Use the most common start time in this group
            var dominantStart = g
                .Where(r => !string.IsNullOrEmpty(r.StartTime))
                .GroupBy(r => r.StartTime!)
                .OrderByDescending(sg => sg.Count())
                .FirstOrDefault()?.Key;

            var minutes = ParseTimeToMinutes(dominantStart);
            groupStartMinutes[g.Key] = minutes;

            if (minutes < currentBaseMinutes)
                currentBaseMinutes = minutes;
        }

        if (currentBaseMinutes == int.MaxValue)
            currentBaseMinutes = 480; // 8:00 AM fallback

        // Step 3: Infer wave per group using current wave size.
        // Use tolerance-based rounding: an offset within 15% of a wave boundary
        // snaps to the nearest wave. This prevents 1-minute time drift from
        // pushing a group into the wrong wave.
        var waveSize = currentMaxGames * currentGsi;
        var groupWaves = new Dictionary<(Guid, string), int>();

        foreach (var (key, startMins) in groupStartMinutes)
        {
            var offset = startMins - currentBaseMinutes;
            int wave;
            if (waveSize > 0)
            {
                wave = 1 + (int)Math.Round((double)offset / waveSize);
            }
            else
            {
                wave = 1;
            }
            if (wave < 1) wave = 1;
            groupWaves[key] = wave;
        }

        // Step 4: Determine new global values
        var newGsi = request.GamestartInterval ?? currentGsi;
        var globalMaxGames = request.MaxGamesPerField ?? currentMaxGames;
        var globalBaseMinutes = request.BaseStartTime != null
            ? ParseTimeToMinutes(request.BaseStartTime)
            : currentBaseMinutes;

        // Build per-DOW overrides lookup (e.g., Saturday → 7:30 AM, Sunday → 8:00 AM)
        var dowOverrides = (request.DowOverrides ?? [])
            .ToDictionary(d => d.Dow, StringComparer.OrdinalIgnoreCase);

        // Step 5: Apply to all rows, resolving per-DOW values where available
        var updated = 0;

        foreach (var g in groups)
        {
            var dow = g.Key.Dow;
            dowOverrides.TryGetValue(dow, out var dowOvr);

            // Per-DOW overrides take precedence over global values
            var newMaxGames = dowOvr?.MaxGamesPerField ?? globalMaxGames;
            var newBaseMinutes = dowOvr?.BaseStartTime != null
                ? ParseTimeToMinutes(dowOvr.BaseStartTime)
                : globalBaseMinutes;

            var wave = groupWaves[g.Key];
            var newStartMinutes = newBaseMinutes + (wave - 1) * newMaxGames * newGsi;
            var newStartTime = MinutesToTimeString(newStartMinutes);

            foreach (var row in g)
            {
                var changed = false;

                // Recalculate start time (wave-adjusted) when any parameter changes
                if (row.StartTime != newStartTime)
                {
                    row.StartTime = newStartTime;
                    changed = true;
                }

                if (request.GamestartInterval.HasValue && row.GamestartInterval != newGsi)
                {
                    row.GamestartInterval = newGsi;
                    changed = true;
                }

                if (row.MaxGamesPerField != newMaxGames)
                {
                    row.MaxGamesPerField = newMaxGames;
                    changed = true;
                }

                if (changed)
                {
                    row.LebUserId = userId;
                    row.Modified = DateTime.UtcNow;
                    updated++;
                }
            }
        }

        return updated;
    }

    /// <summary>
    /// Per-agegroup-per-DOW overrides: apply StartTime and MaxGamesPerField to
    /// rows matching each (AgegroupId, Dow) pair. Wave adjustment is applied
    /// using the group's inferred wave number.
    /// </summary>
    /// <summary>Normalize abbreviated DOW (e.g. "Sat") to full name ("Saturday").</summary>
    private static readonly Dictionary<string, string> DowAbbrevToFull = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sun"] = "sunday", ["mon"] = "monday", ["tue"] = "tuesday", ["wed"] = "wednesday",
        ["thu"] = "thursday", ["fri"] = "friday", ["sat"] = "saturday"
    };

    private static string NormalizeDow(string dow)
    {
        var lower = dow.ToLowerInvariant();
        // Already a full name?
        if (lower is "sunday" or "monday" or "tuesday" or "wednesday"
            or "thursday" or "friday" or "saturday")
            return lower;
        // Try abbreviation lookup (3-char)
        if (lower.Length >= 3 && DowAbbrevToFull.TryGetValue(lower[..3], out var full))
            return full;
        return lower;
    }

    private static int ApplyAgDowOverrides(
        List<TimeslotsLeagueSeasonFields> allRows,
        List<AgDowFieldConfigEntry> overrides,
        string userId)
    {
        // Build lookup: (AgegroupId, Dow) → override — normalize abbreviated DOW to full name
        var lookup = new Dictionary<(Guid, string), AgDowFieldConfigEntry>();
        foreach (var ovr in overrides)
        {
            lookup[(ovr.AgegroupId, NormalizeDow(ovr.Dow))] = ovr;
        }

        // Group rows by (AgegroupId, Dow) — DB stores full names ("Saturday")
        var groups = allRows
            .GroupBy(r => (r.AgegroupId, Dow: r.Dow.ToLowerInvariant()))
            .ToList();

        var updated = 0;

        foreach (var g in groups)
        {
            if (!lookup.TryGetValue(g.Key, out var ovr))
                continue;

            // --- Infer current wave structure (same logic as ApplyUniformConfig) ---

            // Current base = earliest start time in group (wave 1)
            var currentBaseMinutes = g
                .Where(r => !string.IsNullOrEmpty(r.StartTime))
                .Select(r => ParseTimeToMinutes(r.StartTime))
                .DefaultIfEmpty(480)
                .Min();

            // Current dominant GSI and MaxGames for wave-size calculation
            var currentGsi = g
                .GroupBy(r => r.GamestartInterval)
                .OrderByDescending(sg => sg.Count())
                .First().Key;
            var currentMaxGames = g
                .GroupBy(r => r.MaxGamesPerField)
                .OrderByDescending(sg => sg.Count())
                .First().Key;
            var currentWaveSize = currentMaxGames * currentGsi;

            // New values from override
            var newMaxGames = ovr.MaxGamesPerField ?? currentMaxGames;
            var newBaseMinutes = ovr.StartTime != null
                ? ParseTimeToMinutes(ovr.StartTime)
                : currentBaseMinutes;
            // GSI may have been updated by ApplyPerAgConfig already; read from first row
            var newGsi = g.First().GamestartInterval;
            var newWaveSize = newMaxGames * newGsi;

            // Infer each row's wave from its offset, then recalculate with new params
            foreach (var row in g)
            {
                // Determine this row's wave number from its current start time offset
                var rowMinutes = ParseTimeToMinutes(row.StartTime);
                var wave = 1;
                if (currentWaveSize > 0)
                {
                    var offset = rowMinutes - currentBaseMinutes;
                    wave = 1 + (int)Math.Round((double)offset / currentWaveSize);
                    if (wave < 1) wave = 1;
                }

                // Recalculate start time preserving wave offset
                var newStartMinutes = newBaseMinutes + (wave - 1) * newWaveSize;
                var newStartTime = MinutesToTimeString(newStartMinutes);

                var changed = false;

                if (row.StartTime != newStartTime)
                {
                    row.StartTime = newStartTime;
                    changed = true;
                }

                if (ovr.MaxGamesPerField.HasValue && row.MaxGamesPerField != newMaxGames)
                {
                    row.MaxGamesPerField = newMaxGames;
                    changed = true;
                }

                if (changed)
                {
                    row.LebUserId = userId;
                    row.Modified = DateTime.UtcNow;
                    updated++;
                }
            }
        }

        return updated;
    }

    /// <summary>Parse a time string (e.g. "8:00 AM", "08:00 AM", "16:30") to minutes from midnight.</summary>
    private static int ParseTimeToMinutes(string? timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr))
            return 480; // 8:00 AM default

        if (DateTime.TryParse(timeStr, out var dt))
            return dt.Hour * 60 + dt.Minute;

        string[] formats = ["h:mm tt", "hh:mm tt", "H:mm", "HH:mm"];
        if (DateTime.TryParseExact(timeStr, formats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed.Hour * 60 + parsed.Minute;
        }

        return 480; // fallback
    }

    /// <summary>Convert minutes from midnight to "h:mm tt" format (no leading zero).</summary>
    private static string MinutesToTimeString(int totalMinutes)
    {
        totalMinutes = Math.Clamp(totalMinutes, 0, 24 * 60 - 1);
        var dt = DateTime.Today.AddMinutes(totalMinutes);
        return dt.ToString("h:mm tt");
    }

    // ── Helpers ──

    /// <summary>Calculate start time offset for a wave.
    /// Wave 1 = baseStartTime, Wave 2 = base + (max × gsi) minutes, etc.</summary>
    private static string CalculateWaveStartTime(string baseStartTime, int wave, int gsi, int maxGames)
    {
        if (wave <= 1) return baseStartTime;

        if (!DateTime.TryParse(baseStartTime, out var baseTime))
            return baseStartTime;

        var offsetMinutes = (wave - 1) * maxGames * gsi;
        var waveTime = baseTime.AddMinutes(offsetMinutes);
        return waveTime.ToString("h:mm tt");
    }

    private static string GetNextDow(string dow)
    {
        var idx = Array.FindIndex(DowCycle, d => d.Equals(dow, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? DowCycle[(idx + 1) % DowCycle.Length] : dow;
    }

    private static TimeslotDateDto MapDateDto(TimeslotsLeagueSeasonDates d) => new()
    {
        Ai = d.Ai,
        AgegroupId = d.AgegroupId,
        GDate = d.GDate,
        Rnd = d.Rnd,
        DivId = d.DivId,
        DivName = d.Div?.DivName
    };

    private static TimeslotFieldDto MapFieldDto(TimeslotsLeagueSeasonFields f) => new()
    {
        Ai = f.Ai,
        AgegroupId = f.AgegroupId,
        FieldId = f.FieldId,
        FieldName = f.Field?.FName ?? "",
        StartTime = f.StartTime ?? "",
        GamestartInterval = f.GamestartInterval,
        MaxGamesPerField = f.MaxGamesPerField,
        Dow = f.Dow,
        DivId = f.DivId,
        DivName = f.Div?.DivName
    };

    // ── Field assignment management ──

    public async Task<SaveFieldAssignmentsResponse> SaveFieldAssignmentsAsync(
        Guid jobId, string userId, SaveFieldAssignmentsRequest request, CancellationToken ct = default)
    {
        var (_, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var totalCreated = 0;
        var totalDeleted = 0;

        foreach (var entry in request.Entries)
        {
            var desiredFieldIds = entry.FieldIds.ToHashSet();

            // Get existing field-timeslot rows for this agegroup
            var existingRows = await _tsRepo.GetFieldTimeslotsByFilterAsync(
                entry.AgegroupId, season, year, ct: ct);

            var currentFieldIds = existingRows.Select(r => r.FieldId).Distinct().ToHashSet();

            // Fields to remove: in current but not in desired
            var fieldsToRemove = currentFieldIds.Except(desiredFieldIds).ToList();

            // Fields to add: in desired but not in current
            var fieldsToAdd = desiredFieldIds.Except(currentFieldIds).ToList();

            // Delete rows for removed fields
            foreach (var fieldId in fieldsToRemove)
            {
                await _tsRepo.DeleteFieldTimeslotsByFieldAsync(
                    entry.AgegroupId, fieldId, season, year, ct);
                totalDeleted += existingRows.Count(r => r.FieldId == fieldId);
            }

            // Add rows for new fields by cloning from existing templates
            if (fieldsToAdd.Count > 0 && existingRows.Count > 0)
            {
                // Group existing rows by (Dow, DivId) to get timing templates
                var templates = existingRows
                    .Where(r => desiredFieldIds.Contains(r.FieldId) || !fieldsToRemove.Contains(r.FieldId))
                    .GroupBy(r => new { r.Dow, r.DivId })
                    .Select(g => g.First())
                    .ToList();

                var newRows = new List<TimeslotsLeagueSeasonFields>();
                foreach (var newFieldId in fieldsToAdd)
                {
                    foreach (var tmpl in templates)
                    {
                        newRows.Add(new TimeslotsLeagueSeasonFields
                        {
                            AgegroupId = entry.AgegroupId,
                            FieldId = newFieldId,
                            DivId = tmpl.DivId,
                            StartTime = tmpl.StartTime,
                            GamestartInterval = tmpl.GamestartInterval,
                            MaxGamesPerField = tmpl.MaxGamesPerField,
                            Dow = tmpl.Dow,
                            Season = season,
                            Year = year,
                            LebUserId = userId,
                            Modified = DateTime.UtcNow
                        });
                    }
                }

                if (newRows.Count > 0)
                {
                    await _tsRepo.AddFieldTimeslotsRangeAsync(newRows, ct);
                    totalCreated += newRows.Count;
                }
            }
        }

        await _tsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveFieldAssignments: created {Created}, deleted {Deleted} field-timeslot rows",
            totalCreated, totalDeleted);

        return new SaveFieldAssignmentsResponse
        {
            RowsCreated = totalCreated,
            RowsDeleted = totalDeleted
        };
    }
}
