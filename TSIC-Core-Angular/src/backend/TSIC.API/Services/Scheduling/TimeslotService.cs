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
    private readonly IJobRepository _jobRepo;
    private readonly IJobLeagueRepository _jobLeagueRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly ILogger<TimeslotService> _logger;

    /// <summary>Day-of-week cycling for clone-field-dow: Mon→Tue→…→Sun→Mon.</summary>
    private static readonly string[] DowCycle =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public TimeslotService(
        ITimeslotRepository tsRepo,
        IJobRepository jobRepo,
        IJobLeagueRepository jobLeagueRepo,
        ISchedulingContextResolver contextResolver,
        ILogger<TimeslotService> logger)
    {
        _tsRepo = tsRepo;
        _jobRepo = jobRepo;
        _jobLeagueRepo = jobLeagueRepo;
        _contextResolver = contextResolver;
        _logger = logger;
    }

    // ── Readiness ──

    public async Task<CanvasReadinessResponse> GetReadinessAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        var data = await _tsRepo.GetReadinessDataAsync(leagueId, season, year, ct);

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
                GameDays = gameDays
            };
        }).ToList();

        // Count fields assigned to this league-season (FieldsLeagueSeason)
        var assignedFieldIds = await _tsRepo.GetAssignedFieldIdsAsync(leagueId, season, ct);

        // Prior-year field defaults: look up sibling job from previous year
        PriorYearFieldDefaults? priorYearDefaults = null;
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
                            StartTime = defaults.StartTime,
                            GamestartInterval = defaults.GamestartInterval,
                            MaxGamesPerField = defaults.MaxGamesPerField,
                            PriorJobName = priorJob.JobName,
                            PriorYear = priorJob.Year
                        };
                    }
                }
            }
        }

        return new CanvasReadinessResponse
        {
            Agegroups = agegroups,
            AssignedFieldCount = assignedFieldIds.Count,
            PriorYearDefaults = priorYearDefaults
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
                TotalSlots = dowFields.TotalMaxGamesSum
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
            var fieldTimeslotsCreated = 0;

            // Calculate wave-based start time offset
            var startTime = CalculateWaveStartTime(
                request.StartTime, entry.Wave, request.GamestartInterval, request.MaxGamesPerField);

            // ① Check for duplicate date
            var existingDates = await _tsRepo.GetDatesByAgegroupAsync(agegroupId, season, year, ct);
            var alreadyHasDate = existingDates.Any(d => d.GDate.Date == request.GDate.Date);

            if (!alreadyHasDate)
            {
                // Auto-calculate round number: max existing + 1
                var maxRnd = existingDates.Count > 0 ? existingDates.Max(d => d.Rnd) : 0;

                _tsRepo.AddDate(new TimeslotsLeagueSeasonDates
                {
                    AgegroupId = agegroupId,
                    GDate = request.GDate,
                    Rnd = maxRnd + 1,
                    Season = season,
                    Year = year,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                });
                dateCreated = true;
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
                FieldTimeslotsCreated = fieldTimeslotsCreated
            });
        }

        // Single save for all agegroups
        await _tsRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Bulk date assign: {Date:yyyy-MM-dd} → {Count} agegroups, {DatesCreated} dates, {FieldsCreated} field timeslots",
            request.GDate,
            entries.Count,
            results.Count(r => r.DateCreated),
            results.Sum(r => r.FieldTimeslotsCreated));

        return new BulkDateAssignResponse { Results = results };
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
        return waveTime.ToString("hh:mm tt");
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
}
