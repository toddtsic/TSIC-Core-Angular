using System.Text.RegularExpressions;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Orchestrates the Auto-Build Entire Schedule feature.
/// Extracts patterns from a prior year's schedule and replays them
/// onto the current year's dates, fields, and pairings.
/// </summary>
public sealed class AutoBuildScheduleService : IAutoBuildScheduleService
{
    private readonly IAutoBuildRepository _autoBuildRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ITimeslotRepository _timeslotRepo;
    private readonly IPairingsRepository _pairingsRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly IAgeGroupRepository _agegroupRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly IScheduleDivisionService _scheduleDivisionService;
    private readonly IScheduleQaService _qaService;
    private readonly ILogger<AutoBuildScheduleService> _logger;

    public AutoBuildScheduleService(
        IAutoBuildRepository autoBuildRepo,
        IScheduleRepository scheduleRepo,
        ITimeslotRepository timeslotRepo,
        IPairingsRepository pairingsRepo,
        IFieldRepository fieldRepo,
        IAgeGroupRepository agegroupRepo,
        IDivisionRepository divisionRepo,
        ISchedulingContextResolver contextResolver,
        IScheduleDivisionService scheduleDivisionService,
        IScheduleQaService qaService,
        ILogger<AutoBuildScheduleService> logger)
    {
        _autoBuildRepo = autoBuildRepo;
        _scheduleRepo = scheduleRepo;
        _timeslotRepo = timeslotRepo;
        _pairingsRepo = pairingsRepo;
        _fieldRepo = fieldRepo;
        _agegroupRepo = agegroupRepo;
        _divisionRepo = divisionRepo;
        _contextResolver = contextResolver;
        _scheduleDivisionService = scheduleDivisionService;
        _qaService = qaService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════
    // Phase 1: Source Job Discovery
    // ══════════════════════════════════════════════════════════

    public async Task<List<AutoBuildSourceJobDto>> GetSourceJobsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _autoBuildRepo.GetSourceJobCandidatesAsync(jobId, ct);
    }

    // ══════════════════════════════════════════════════════════
    // Phase 2-3: Analysis + Feasibility
    // ══════════════════════════════════════════════════════════

    public async Task<AutoBuildAnalysisResponse> AnalyzeAsync(
        Guid jobId, Guid sourceJobId, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        // 1. Get source info
        var sourceJobName = await _autoBuildRepo.GetJobNameAsync(sourceJobId, ct) ?? "";
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct) ?? "";
        var sourcePattern = await _autoBuildRepo.ExtractPatternAsync(sourceJobId, ct);

        // 2. Get division summaries from both sides
        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(sourceJobId, ct);
        var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);

        // 3. Match divisions
        var matches = MatchDivisions(sourceDivisions, currentDivisions);

        // 4. Check field availability
        var sourceFieldNames = await _autoBuildRepo.GetSourceFieldNamesAsync(sourceJobId, ct);
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var currentFieldNames = currentFields.Select(f => f.FName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fieldMismatches = sourceFieldNames
            .Where(sf => !currentFieldNames.Contains(sf))
            .ToList();

        // 5. Compute feasibility
        var totalCurrent = matches.Count(m => m.MatchType != DivisionMatchType.RemovedDivision);
        var exactMatches = matches.Count(m => m.MatchType == DivisionMatchType.ExactMatch);
        var sizeMismatches = matches.Count(m => m.MatchType == DivisionMatchType.SizeMismatch);
        var newDivisions = matches.Count(m => m.MatchType == DivisionMatchType.NewDivision);
        var removedDivisions = matches.Count(m => m.MatchType == DivisionMatchType.RemovedDivision);

        var confidencePercent = totalCurrent > 0
            ? (int)Math.Round(100.0 * exactMatches / totalCurrent)
            : 0;

        var confidenceLevel = confidencePercent switch
        {
            > 80 => "green",
            > 50 => "yellow",
            _ => "red"
        };

        var warnings = new List<string>();
        if (fieldMismatches.Count > 0)
            warnings.Add($"{fieldMismatches.Count} field(s) from prior year not found in current setup: {string.Join(", ", fieldMismatches)}");
        if (newDivisions > 0)
            warnings.Add($"{newDivisions} new division(s) will use standard auto-schedule (no prior pattern).");
        if (sizeMismatches > 0)
            warnings.Add($"{sizeMismatches} division(s) have different team counts — you'll choose how to handle each.");

        return new AutoBuildAnalysisResponse
        {
            SourceJobId = sourceJobId,
            SourceJobName = sourceJobName,
            SourceYear = sourceYear,
            SourceTotalGames = sourcePattern.Count,
            DivisionMatches = matches,
            Feasibility = new AutoBuildFeasibility
            {
                TotalCurrentDivisions = totalCurrent,
                ExactMatches = exactMatches,
                SizeMismatches = sizeMismatches,
                NewDivisions = newDivisions,
                RemovedDivisions = removedDivisions,
                ConfidenceLevel = confidenceLevel,
                ConfidencePercent = confidencePercent,
                FieldMismatches = fieldMismatches,
                Warnings = warnings
            }
        };
    }

    // ══════════════════════════════════════════════════════════
    // Phase 6-7: Build
    // ══════════════════════════════════════════════════════════

    public async Task<AutoBuildResult> BuildAsync(
        Guid jobId, string userId, AutoBuildRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // 1. Extract the source pattern
        var pattern = await _autoBuildRepo.ExtractPatternAsync(request.SourceJobId, ct);
        var patternByDiv = pattern
            .GroupBy(p => (p.AgegroupName, p.DivName))
            .ToDictionary(g => g.Key, g => g.ToList());

        // 2. Get current divisions and build the match list
        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(request.SourceJobId, ct);
        var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var matches = MatchDivisions(sourceDivisions, currentDivisions);

        // 3. Get current timeslot dates (sorted for DayOrdinal mapping)
        var currentDatesByAgegroup = new Dictionary<Guid, List<DateTime>>();

        // 4. Get current fields for name matching
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var fieldNameToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in currentFields)
            fieldNameToId.TryAdd(f.FName, f.FieldId);

        // Pre-load field name map (FieldId → FName) for Schedule record FName population
        var fieldIdToName = currentFields.ToDictionary(f => f.FieldId, f => f.FName);

        // 5. Get skip/resolution sets from user input
        var skipIds = request.SkipDivisionIds?.ToHashSet() ?? [];
        var resolutions = (request.MismatchResolutions ?? [])
            .ToDictionary(r => r.DivId, r => r.Strategy);

        // 6. Get existing game counts for partial-schedule detection
        var existingCounts = await _autoBuildRepo.GetExistingGameCountsByDivisionAsync(jobId, ct);

        // 7. Initialize occupied slots for the entire job
        var allFieldIds = currentFields.Select(f => f.FieldId).ToList();
        var occupiedSlots = allFieldIds.Count > 0
            ? await _scheduleRepo.GetOccupiedSlotsAsync(jobId, allFieldIds, ct)
            : new HashSet<(Guid fieldId, DateTime gDate)>();

        var divisionResults = new List<AutoBuildDivisionResult>();
        int totalPlaced = 0;
        int totalFailed = 0;

        // 8. Process each matchable division
        var actionableDivisions = matches
            .Where(m => m.MatchType != DivisionMatchType.RemovedDivision
                        && m.CurrentDivId.HasValue)
            .OrderBy(m => m.AgegroupName)
            .ThenBy(m => m.DivName)
            .ToList();

        foreach (var match in actionableDivisions)
        {
            var divId = match.CurrentDivId!.Value;
            var agegroupId = match.CurrentAgegroupId!.Value;

            // Skip if user chose to skip
            if (skipIds.Contains(divId))
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = match.AgegroupName,
                    DivName = match.DivName,
                    DivId = divId,
                    GamesPlaced = 0,
                    GamesFailed = 0,
                    Status = "skipped"
                });
                continue;
            }

            // Skip if already scheduled and user chose to preserve
            if (request.SkipAlreadyScheduled && existingCounts.GetValueOrDefault(divId) > 0)
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = match.AgegroupName,
                    DivName = match.DivName,
                    DivId = divId,
                    GamesPlaced = 0,
                    GamesFailed = 0,
                    Status = "already-scheduled"
                });
                continue;
            }

            // Determine strategy
            var strategy = match.MatchType switch
            {
                DivisionMatchType.ExactMatch => "pattern-replay",
                DivisionMatchType.SizeMismatch => resolutions.GetValueOrDefault(divId, "auto-schedule"),
                DivisionMatchType.NewDivision => "auto-schedule",
                _ => "skip"
            };

            if (strategy == "skip")
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = match.AgegroupName,
                    DivName = match.DivName,
                    DivId = divId,
                    GamesPlaced = 0,
                    GamesFailed = 0,
                    Status = "skipped"
                });
                continue;
            }

            // Delete existing games for this division before rebuilding
            if (existingCounts.GetValueOrDefault(divId) > 0)
            {
                await _scheduleRepo.DeleteDivisionGamesAsync(divId, leagueId, season, year, ct);
                await _scheduleRepo.SaveChangesAsync(ct);
            }

            if (strategy == "auto-schedule")
            {
                // Fallback: use existing per-division auto-schedule
                var result = await _scheduleDivisionService.AutoScheduleDivAsync(jobId, userId, divId, ct);

                // Update occupied slots with newly placed games
                if (result.ScheduledCount > 0)
                {
                    var newOccupied = await _scheduleRepo.GetOccupiedSlotsAsync(jobId, allFieldIds, ct);
                    occupiedSlots = newOccupied;
                }

                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = match.AgegroupName,
                    DivName = match.DivName,
                    DivId = divId,
                    GamesPlaced = result.ScheduledCount,
                    GamesFailed = result.FailedCount,
                    Status = "auto-schedule"
                });
                totalPlaced += result.ScheduledCount;
                totalFailed += result.FailedCount;
            }
            else // pattern-replay (or use-current-pairings for size mismatch)
            {
                var (placed, failed) = await ReplayPatternForDivisionAsync(
                    jobId, userId, leagueId, season, year,
                    agegroupId, divId, match,
                    patternByDiv, fieldNameToId, fieldIdToName,
                    currentDatesByAgegroup, occupiedSlots,
                    request.IncludeBracketGames, ct);

                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = match.AgegroupName,
                    DivName = match.DivName,
                    DivId = divId,
                    GamesPlaced = placed,
                    GamesFailed = failed,
                    Status = "pattern-replay"
                });
                totalPlaced += placed;
                totalFailed += failed;
            }
        }

        var scheduled = divisionResults.Count(r => r.Status != "skipped" && r.Status != "already-scheduled");
        var skipped = divisionResults.Count(r => r.Status == "skipped" || r.Status == "already-scheduled");

        _logger.LogInformation(
            "AutoBuild: Job={JobId}, Divisions={Total}, Scheduled={Scheduled}, Skipped={Skipped}, " +
            "GamesPlaced={Placed}, GamesFailed={Failed}",
            jobId, actionableDivisions.Count, scheduled, skipped, totalPlaced, totalFailed);

        return new AutoBuildResult
        {
            TotalDivisions = actionableDivisions.Count,
            DivisionsScheduled = scheduled,
            DivisionsSkipped = skipped,
            TotalGamesPlaced = totalPlaced,
            GamesFailedToPlace = totalFailed,
            DivisionResults = divisionResults
        };
    }

    // ══════════════════════════════════════════════════════════
    // Undo
    // ══════════════════════════════════════════════════════════

    public async Task<int> UndoAsync(Guid jobId, CancellationToken ct = default)
    {
        var count = await _autoBuildRepo.DeleteAllGamesForJobAsync(jobId, ct);
        _logger.LogInformation("AutoBuild Undo: Job={JobId}, GamesDeleted={Count}", jobId, count);
        return count;
    }

    public async Task<AutoBuildQaResult> ValidateAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _qaService.RunValidationAsync(jobId, ct);
    }

    // ══════════════════════════════════════════════════════════
    // Private: Pattern Replay for a Single Division
    // ══════════════════════════════════════════════════════════

    private async Task<(int placed, int failed)> ReplayPatternForDivisionAsync(
        Guid jobId, string userId, Guid leagueId, string season, string year,
        Guid agegroupId, Guid divId, DivisionMatch match,
        Dictionary<(string AgegroupName, string DivName), List<GamePlacementPattern>> patternByDiv,
        Dictionary<string, Guid> fieldNameToId,
        Dictionary<Guid, string> fieldIdToName,
        Dictionary<Guid, List<DateTime>> currentDatesByAgegroup,
        HashSet<(Guid fieldId, DateTime gDate)> occupiedSlots,
        bool includeBracketGames,
        CancellationToken ct)
    {
        // Find matching pattern using normalized agegroup name
        var patternKey = FindPatternKey(match.AgegroupName, match.DivName, patternByDiv);
        if (patternKey == null)
        {
            _logger.LogWarning("AutoBuild: No pattern found for {Agegroup}/{Div}", match.AgegroupName, match.DivName);
            return (0, 0);
        }

        var placements = patternByDiv[patternKey.Value];

        // Filter to round-robin only unless bracket games requested
        if (!includeBracketGames)
            placements = placements.Where(p => p.T1Type == "T" && p.T2Type == "T").ToList();

        if (placements.Count == 0)
            return (0, 0);

        // Get current year's timeslot dates for this agegroup
        if (!currentDatesByAgegroup.TryGetValue(agegroupId, out var currentDates))
        {
            var dates = await _timeslotRepo.GetDatesAsync(agegroupId, season, year, ct);
            var divDates = dates.Where(d => d.DivId == divId).ToList();
            var effectiveDates = divDates.Count > 0
                ? divDates
                : dates.Where(d => d.DivId == null).ToList();

            currentDates = effectiveDates
                .Select(d => d.GDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            currentDatesByAgegroup[agegroupId] = currentDates;
        }

        // Build DayOrdinal → current date map
        var dayOrdinalToDate = new Dictionary<int, DateTime>();
        for (var i = 0; i < currentDates.Count; i++)
            dayOrdinalToDate[i] = currentDates[i];

        // Get division metadata
        var agegroup = await _agegroupRepo.GetByIdAsync(agegroupId, ct);
        var division = await _divisionRepo.GetByIdReadOnlyAsync(divId, ct);
        var agSeason = agegroup?.Season ?? season;
        var agLeagueId = agegroup?.LeagueId ?? leagueId;

        // Get current pairings for this division
        var teamCount = await _pairingsRepo.GetDivisionTeamCountAsync(divId, jobId, ct);
        var pairings = await _pairingsRepo.GetPairingsAsync(agLeagueId, agSeason, teamCount, ct);
        var rrPairings = pairings
            .Where(p => p.T1Type == "T" && p.T2Type == "T")
            .ToList();

        // Build pairing lookup by (Rnd, GameNumber)
        var pairingLookup = rrPairings
            .ToDictionary(p => (p.Rnd, p.GameNumber), p => p);

        // Get fallback timeslot data for FindNextAvailableTimeslot
        var allDates = await _timeslotRepo.GetDatesAsync(agegroupId, season, year, ct);
        var divDatesForFallback = allDates.Where(d => d.DivId == divId).ToList();
        var effectiveDatesForFallback = divDatesForFallback.Count > 0
            ? divDatesForFallback
            : allDates.Where(d => d.DivId == null).ToList();

        var allFields = await _timeslotRepo.GetFieldTimeslotsAsync(agegroupId, season, year, ct);
        var divFields = allFields.Where(f => f.DivId == divId).ToList();
        var effectiveFields = divFields.Count > 0
            ? divFields
            : allFields.Where(f => f.DivId == null).ToList();

        int placed = 0;
        int failed = 0;

        foreach (var placement in placements)
        {
            // Try to match to a current-year pairing
            PairingsLeagueSeason? pairing = null;
            if (placement.T1Type == "T" && placement.T2Type == "T")
            {
                pairingLookup.TryGetValue((placement.Rnd, placement.GameNumber), out pairing);
            }

            // If no matching pairing found (team count changed), skip this placement
            if (pairing == null && placement.T1Type == "T" && placement.T2Type == "T")
            {
                failed++;
                continue;
            }

            // Map DayOrdinal → current date
            if (!dayOrdinalToDate.TryGetValue(placement.DayOrdinal, out var targetDate))
            {
                // DayOrdinal exceeds current dates — try fallback
                var fallbackSlot = FindNextAvailableTimeslot(effectiveDatesForFallback, effectiveFields, occupiedSlots);
                if (fallbackSlot == null) { failed++; continue; }

                targetDate = fallbackSlot.Value.gDate.Date;
            }

            // Map field name → current FieldId
            Guid targetFieldId;
            if (fieldNameToId.TryGetValue(placement.FieldName, out var matchedFieldId))
            {
                targetFieldId = matchedFieldId;
            }
            else
            {
                // Field not found — use fallback
                var fallbackSlot = FindNextAvailableTimeslot(effectiveDatesForFallback, effectiveFields, occupiedSlots);
                if (fallbackSlot == null) { failed++; continue; }

                targetFieldId = fallbackSlot.Value.fieldId;
                targetDate = fallbackSlot.Value.gDate.Date;
            }

            // Construct the target game datetime
            var targetGDate = targetDate + placement.TimeOfDay;

            // Check if slot is occupied
            if (occupiedSlots.Contains((targetFieldId, targetGDate)))
            {
                // Try fallback
                var fallbackSlot = FindNextAvailableTimeslot(effectiveDatesForFallback, effectiveFields, occupiedSlots);
                if (fallbackSlot == null) { failed++; continue; }

                targetFieldId = fallbackSlot.Value.fieldId;
                targetGDate = fallbackSlot.Value.gDate;
            }

            // Create the Schedule record
            var game = new Schedule
            {
                JobId = jobId,
                LeagueId = agLeagueId,
                Season = agSeason,
                Year = year,
                AgegroupId = agegroupId,
                AgegroupName = agegroup?.AgegroupName ?? "",
                DivId = divId,
                DivName = division?.DivName ?? "",
                Div2Id = divId,
                FieldId = targetFieldId,
                FName = fieldIdToName.GetValueOrDefault(targetFieldId, ""),
                GDate = targetGDate,
                GNo = pairing?.GameNumber ?? placement.GameNumber,
                GStatusCode = 1, // Scheduled
                Rnd = (byte)(pairing?.Rnd ?? placement.Rnd),
                T1No = pairing?.T1 ?? 0,
                T1Type = pairing?.T1Type ?? placement.T1Type,
                T2No = (byte)(pairing?.T2 ?? 0),
                T2Type = pairing?.T2Type ?? placement.T2Type,
                T1Ann = pairing?.T1Annotation,
                T1CalcType = pairing?.T1CalcType,
                T1GnoRef = pairing?.T1GnoRef,
                T2Ann = pairing?.T2Annotation,
                T2CalcType = pairing?.T2CalcType,
                T2GnoRef = pairing?.T2GnoRef,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            };

            _scheduleRepo.AddGame(game);
            await _scheduleRepo.SaveChangesAsync(ct);

            occupiedSlots.Add((targetFieldId, targetGDate));
            placed++;
        }

        // Bulk resolve team names for the entire division
        await _scheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(divId, jobId, ct);

        return (placed, failed);
    }

    // ══════════════════════════════════════════════════════════
    // Private: Division Matching
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Match source divisions to current divisions using normalized agegroup names.
    /// </summary>
    private static List<DivisionMatch> MatchDivisions(
        List<SourceDivisionSummary> sourceDivisions,
        List<CurrentDivisionSummary> currentDivisions)
    {
        var matches = new List<DivisionMatch>();
        var matchedCurrentIds = new HashSet<Guid>();

        // Build lookup: normalized source agegroup name → list of divisions
        // Apply year-increment to source names to match current year names
        var currentLookup = currentDivisions
            .GroupBy(c => (c.AgegroupName, c.DivName))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var source in sourceDivisions)
        {
            var normalizedName = IncrementYearsInName(source.AgegroupName);

            if (currentLookup.TryGetValue((normalizedName, source.DivName), out var current))
            {
                matchedCurrentIds.Add(current.DivId);
                matches.Add(new DivisionMatch
                {
                    AgegroupName = normalizedName,
                    DivName = source.DivName,
                    CurrentDivId = current.DivId,
                    CurrentAgegroupId = current.AgegroupId,
                    SourceTeamCount = source.TeamCount,
                    CurrentTeamCount = current.TeamCount,
                    MatchType = source.TeamCount == current.TeamCount
                        ? DivisionMatchType.ExactMatch
                        : DivisionMatchType.SizeMismatch,
                    SourceGameCount = source.GameCount
                });
            }
            else
            {
                // Source division not found in current year
                matches.Add(new DivisionMatch
                {
                    AgegroupName = normalizedName,
                    DivName = source.DivName,
                    CurrentDivId = null,
                    CurrentAgegroupId = null,
                    SourceTeamCount = source.TeamCount,
                    CurrentTeamCount = null,
                    MatchType = DivisionMatchType.RemovedDivision,
                    SourceGameCount = source.GameCount
                });
            }
        }

        // Find new divisions (exist this year but not last year)
        foreach (var current in currentDivisions)
        {
            if (!matchedCurrentIds.Contains(current.DivId))
            {
                matches.Add(new DivisionMatch
                {
                    AgegroupName = current.AgegroupName,
                    DivName = current.DivName,
                    CurrentDivId = current.DivId,
                    CurrentAgegroupId = current.AgegroupId,
                    SourceTeamCount = 0,
                    CurrentTeamCount = current.TeamCount,
                    MatchType = DivisionMatchType.NewDivision,
                    SourceGameCount = 0
                });
            }
        }

        return matches;
    }

    /// <summary>
    /// Find the pattern key for a division, trying both the exact name
    /// and the year-decremented name (to match source pattern data).
    /// </summary>
    private static (string AgegroupName, string DivName)? FindPatternKey(
        string agegroupName, string divName,
        Dictionary<(string AgegroupName, string DivName), List<GamePlacementPattern>> patternByDiv)
    {
        // Try exact match first (normalized name = current year)
        if (patternByDiv.ContainsKey((agegroupName, divName)))
            return (agegroupName, divName);

        // Try year-decremented name (to match source data)
        var decremented = DecrementYearsInName(agegroupName);
        if (patternByDiv.ContainsKey((decremented, divName)))
            return (decremented, divName);

        return null;
    }

    // ══════════════════════════════════════════════════════════
    // Private: FindNextAvailableTimeslot (same as ScheduleDivisionService)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Find the next available timeslot by walking dates, fields, and game intervals.
    /// Matches the algorithm in ScheduleDivisionService.
    /// </summary>
    private static (Guid fieldId, DateTime gDate)? FindNextAvailableTimeslot(
        List<TimeslotDateDto> dates,
        List<TimeslotFieldDto> fields,
        HashSet<(Guid fieldId, DateTime gDate)> occupiedSlots)
    {
        foreach (var date in dates.OrderBy(d => d.GDate))
        {
            var dow = date.GDate.DayOfWeek.ToString();
            var dowFields = fields
                .Where(f => f.Dow.Equals(dow, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.FieldId)
                .ToList();

            foreach (var ft in dowFields)
            {
                if (!TimeSpan.TryParse(ft.StartTime, out var startTime))
                    continue;

                var baseDate = date.GDate.Date;

                for (var g = 0; g < ft.MaxGamesPerField; g++)
                {
                    var gameTime = baseDate + startTime + TimeSpan.FromMinutes(g * ft.GamestartInterval);
                    if (!occupiedSlots.Contains((ft.FieldId, gameTime)))
                        return (ft.FieldId, gameTime);
                }
            }
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════
    // Private: Year Name Utilities
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Finds 4-digit year patterns (2020–2039) in a string and increments each by 1.
    /// Replicates the logic from JobCloneService.IncrementYearsInName.
    /// </summary>
    private static string IncrementYearsInName(string name)
    {
        return Regex.Replace(name, @"\b(20[2-3]\d)\b", m =>
            (int.Parse(m.Value) + 1).ToString());
    }

    /// <summary>
    /// Reverse of IncrementYearsInName: decrements year patterns by 1.
    /// Used to match current-year names back to source-year pattern data.
    /// </summary>
    private static string DecrementYearsInName(string name)
    {
        return Regex.Replace(name, @"\b(20[2-3]\d)\b", m =>
            (int.Parse(m.Value) - 1).ToString());
    }
}
