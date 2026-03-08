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
    private readonly IAgeGroupRepository _agegroupRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly IDivisionProfileRepository _divisionProfileRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly IScheduleDivisionService _scheduleDivisionService;
    private readonly IScheduleQaService _qaService;
    private readonly IPairingsService _pairingsService;
    private readonly ILogger<AutoBuildScheduleService> _logger;

    public AutoBuildScheduleService(
        IAutoBuildRepository autoBuildRepo,
        IScheduleRepository scheduleRepo,
        ITimeslotRepository timeslotRepo,
        IPairingsRepository pairingsRepo,
        IAgeGroupRepository agegroupRepo,
        IDivisionRepository divisionRepo,
        IDivisionProfileRepository divisionProfileRepo,
        IFieldRepository fieldRepo,
        ISchedulingContextResolver contextResolver,
        IScheduleDivisionService scheduleDivisionService,
        IScheduleQaService qaService,
        IPairingsService pairingsService,
        ILogger<AutoBuildScheduleService> logger)
    {
        _autoBuildRepo = autoBuildRepo;
        _scheduleRepo = scheduleRepo;
        _timeslotRepo = timeslotRepo;
        _pairingsRepo = pairingsRepo;
        _agegroupRepo = agegroupRepo;
        _divisionRepo = divisionRepo;
        _divisionProfileRepo = divisionProfileRepo;
        _fieldRepo = fieldRepo;
        _contextResolver = contextResolver;
        _scheduleDivisionService = scheduleDivisionService;
        _qaService = qaService;
        _pairingsService = pairingsService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════
    // Game Summary (current schedule status)
    // ══════════════════════════════════════════════════════════

    public async Task<GameSummaryResponse> GetGameSummaryAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var jobName = await _autoBuildRepo.GetJobNameAsync(jobId, ct) ?? "this job";
        var divisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var gameCounts = await _scheduleRepo.GetRoundRobinGameCountsByDivisionAsync(jobId, ct);

        // Get actual pairing counts per pool size (team count) from PairingsLeagueSeason
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
        var pairingsByPoolSize = await _pairingsRepo.GetRoundRobinPairingCountsByPoolSizeAsync(
            leagueId, season, ct);

        // Get max round per pool size — this reflects the scheduler's game guarantee
        var maxRoundByPoolSize = await _pairingsRepo.GetMaxRoundByPoolSizeAsync(
            leagueId, season, ct);

        // Filter out inactive agegroups (WAITLIST, DROPPED) and placeholder/dropped divisions
        var activeDivisions = FilterSchedulableDivisions(divisions);

        var summaries = activeDivisions.Select(d =>
        {
            var gameCount = gameCounts.GetValueOrDefault(d.DivId);
            // Use actual pairing count from PairingsLeagueSeason, fall back to formula
            var expectedGames = pairingsByPoolSize.GetValueOrDefault(
                d.TeamCount, d.TeamCount * (d.TeamCount - 1) / 2);
            return new ScheduleGameSummaryDto
            {
                AgegroupName = d.AgegroupName,
                AgegroupId = d.AgegroupId,
                AgegroupColor = d.AgegroupColor,
                DivName = d.DivName,
                DivId = d.DivId,
                TeamCount = d.TeamCount,
                GameCount = gameCount,
                ExpectedRrGames = expectedGames
            };
        }).ToList();

        var totalGames = summaries.Sum(s => s.GameCount);
        var divsWithGames = summaries.Count(s => s.GameCount > 0);

        // Derive effective game guarantee from pairing table round counts.
        // For each pool size: even TCnt → guarantee = maxRound, odd → guarantee = maxRound - 1.
        // The event-level guarantee is the minimum across all pool sizes.
        int? derivedGameGuarantee = null;
        if (maxRoundByPoolSize.Count > 0)
        {
            derivedGameGuarantee = maxRoundByPoolSize
                .Select(kvp =>
                {
                    var tCnt = kvp.Key;
                    var maxRound = kvp.Value;
                    return tCnt % 2 == 0 ? maxRound : maxRound - 1;
                })
                .Min();
        }

        return new GameSummaryResponse
        {
            JobName = jobName,
            TotalGames = totalGames,
            TotalDivisions = summaries.Count,
            DivisionsWithGames = divsWithGames,
            Divisions = summaries,
            GameGuarantee = derivedGameGuarantee
        };
    }

    // ══════════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════════
    // Undo
    // ══════════════════════════════════════════════════════════

    public async Task<int> UndoAsync(Guid jobId, CancellationToken ct = default)
    {
        var count = await _autoBuildRepo.DeleteAllGamesForJobAsync(jobId, ct);
        _logger.LogInformation("AutoBuild Undo: Job={JobId}, GamesDeleted={Count}", jobId, count);
        return count;
    }

    // ══════════════════════════════════════════════════════════
    // Prerequisite Checks
    // ══════════════════════════════════════════════════════════

    public async Task<PrerequisiteCheckResponse> CheckPrerequisitesAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // 1. Pools: active teams without a division assignment
        var unassignedCount = await _autoBuildRepo.GetUnassignedActiveTeamCountAsync(jobId, ct);
        var poolsAssigned = unassignedCount == 0;

        // 2. Pairings: every distinct TCnt in schedulable divisions has pairings
        //    Exclude WAITLIST/DROPPED agegroups and placeholder/dropped divisions
        var divisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var schedulableDivisions = FilterSchedulableDivisions(divisions);
        var distinctTCnts = schedulableDivisions
            .Where(d => d.TeamCount > 0)
            .Select(d => d.TeamCount)
            .Distinct()
            .ToList();

        var tcntsWithPairings = await _pairingsRepo
            .GetDistinctPoolSizesWithPairingsAsync(leagueId, season, ct);

        var missingTCnts = distinctTCnts
            .Where(t => !tcntsWithPairings.Contains(t))
            .OrderBy(t => t)
            .ToList();

        var pairingsCreated = missingTCnts.Count == 0;

        // 2b. Current rounds for existing pairings (sequential — shared DbContext)
        var existingRounds = new Dictionary<int, int>();
        foreach (var tCnt in distinctTCnts.Where(t => tcntsWithPairings.Contains(t)))
        {
            var (_, maxRound) = await _pairingsRepo.GetMaxGameAndRoundAsync(leagueId, season, tCnt, ct);
            existingRounds[tCnt] = maxRound;
        }

        // 3. Timeslots: every agegroup with active divisions has at least one date
        var agegroupsMissing = await _autoBuildRepo
            .GetAgegroupsMissingTimeslotDatesAsync(jobId, season, year, ct);

        var timeslotsConfigured = agegroupsMissing.Count == 0;

        return new PrerequisiteCheckResponse
        {
            PoolsAssigned = poolsAssigned,
            UnassignedTeamCount = unassignedCount,
            PairingsCreated = pairingsCreated,
            MissingPairingTCnts = missingTCnts,
            ExistingPairingRounds = existingRounds,
            TimeslotsConfigured = timeslotsConfigured,
            AgegroupsMissingTimeslots = agegroupsMissing,
            AllPassed = poolsAssigned && pairingsCreated && timeslotsConfigured
        };
    }

    // ══════════════════════════════════════════════════════════
    // Profile Extraction (Q1–Q10)
    // ══════════════════════════════════════════════════════════

    public async Task<ProfileExtractionResponse> ExtractProfilesAsync(
        Guid jobId, Guid sourceJobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Extract raw patterns and division summaries from source
        var patterns = await _autoBuildRepo.ExtractPatternAsync(sourceJobId, ct);
        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(sourceJobId, ct);

        // Apply graduation year offset so source "2026 Boys" matches current "2027 Boys"
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct);
        var yearDelta = ComputeYearDelta(sourceYear, year);
        if (yearDelta != 0)
            sourceDivisions = RemapSourceDivisionNames(sourceDivisions, yearDelta);

        // Get source job's timeslot window (earliest field start per DOW)
        var sourceWindow = await GetSourceTimeslotWindowAsync(sourceJobId, ct);

        // Get current-year division counts per TCnt (for DivisionCount in profile)
        var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var currentDivCountByTCnt = currentDivisions
            .GroupBy(d => d.TeamCount)
            .ToDictionary(g => g.Key, g => g.Count());

        // Compute Q1–Q10 profiles (pure computation, no DB)
        var profiles = AttributeExtractor.ExtractProfiles(
            patterns, sourceDivisions, currentDivCountByTCnt, sourceWindow);

        // Translate source field names → current field names (address-based mapping)
        var fieldMap = await BuildFieldNameMapAsync(patterns, leagueId, season, ct);
        if (fieldMap.Count > 0)
        {
            profiles = profiles.ToDictionary(
                kvp => kvp.Key,
                kvp => ApplyFieldNameMap(kvp.Value, fieldMap));
        }

        // Pre-flight disconnects: compare source discoveries against current timeslot canvas
        var disconnects = await CheckDisconnectsAsync(jobId, profiles, sourceJobId, ct);

        // Get source job metadata
        var sourceName = await _autoBuildRepo.GetJobNameAsync(sourceJobId, ct) ?? "";

        return new ProfileExtractionResponse
        {
            SourceJobId = sourceJobId,
            SourceJobName = sourceName,
            SourceYear = sourceYear ?? "",
            Profiles = profiles.Values
                .OrderBy(p => p.TCnt)
                .ToList(),
            Disconnects = disconnects.Count > 0 ? disconnects : null
        };
    }

    // ══════════════════════════════════════════════════════════
    // Build — Horizontal-First Placement with Scoring Engine
    // ══════════════════════════════════════════════════════════

    public async Task<AutoBuildResult> BuildAsync(
        Guid jobId, string userId, AutoBuildRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        var keepExisting = string.Equals(
            request.ExistingGameMode, "keep", StringComparison.OrdinalIgnoreCase);

        // ── 1. Extract profiles ──
        // Strategy-driven path: user provided explicit DivisionStrategies
        var useStrategyPath = request.DivisionStrategies is { Count: > 0 };
        var strategyByName = useStrategyPath
            ? request.DivisionStrategies!.ToDictionary(
                s => s.DivisionName, s => s, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DivisionStrategyEntry>(StringComparer.OrdinalIgnoreCase);

        Dictionary<int, DivisionSizeProfile> profilesByTCnt;
        Dictionary<DayOfWeek, TimeSpan>? sourceTimeslotWindow = null;

        if (!useStrategyPath && request.SourceJobId.HasValue)
        {
            // Legacy TCnt-keyed extraction path (backward compatibility)
            var patterns = await _autoBuildRepo.ExtractPatternAsync(request.SourceJobId.Value, ct);
            var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(
                request.SourceJobId.Value, ct);

            // Apply graduation year offset so source "2026 Boys" matches current "2027 Boys"
            var srcYear = await _autoBuildRepo.GetJobYearAsync(request.SourceJobId.Value, ct);
            var yrDelta = ComputeYearDelta(srcYear, year);
            if (yrDelta != 0)
                sourceDivisions = RemapSourceDivisionNames(sourceDivisions, yrDelta);

            sourceTimeslotWindow = await GetSourceTimeslotWindowAsync(request.SourceJobId.Value, ct);
            var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
            var currentDivCountByTCnt = currentDivisions
                .GroupBy(d => d.TeamCount)
                .ToDictionary(g => g.Key, g => g.Count());

            profilesByTCnt = AttributeExtractor.ExtractProfiles(
                patterns, sourceDivisions, currentDivCountByTCnt, sourceTimeslotWindow);

            // Translate source field names → current field names (address-based mapping)
            var fieldMap = await BuildFieldNameMapAsync(patterns, leagueId, season, ct);
            if (fieldMap.Count > 0)
            {
                profilesByTCnt = profilesByTCnt.ToDictionary(
                    kvp => kvp.Key,
                    kvp => ApplyFieldNameMap(kvp.Value, fieldMap));
            }
        }
        else
        {
            // Clean sheet or strategy-driven mode — profiles built per-division
            profilesByTCnt = new Dictionary<int, DivisionSizeProfile>();
        }

        // ── 2. Get current divisions and filter ──
        var allDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var activeDivisions = FilterSchedulableDivisions(allDivisions);

        var excludedIds = request.ExcludedDivisionIds.ToHashSet();

        // ── 3. Initialize global state ──
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var allFieldIds = currentFields.Select(f => f.FieldId).ToList();

        // Load field preferences for Avoid/Preferred scoring
        var fieldPreferences = await _fieldRepo.GetFieldPreferencesAsync(leagueId, season, ct);

        var occupiedSlots = allFieldIds.Count > 0
            ? await _scheduleRepo.GetOccupiedSlotsAsync(jobId, allFieldIds, ct)
            : new HashSet<(Guid fieldId, DateTime gDate)>();

        var existingCounts = await _autoBuildRepo.GetExistingGameCountsByDivisionAsync(jobId, ct);

        // ── 4. Build processing order: agegroups → divisions ──
        // Build wave lookup: agegroupId → wave number
        var waveByAgId = request.AgegroupOrder
            .ToDictionary(e => e.AgegroupId, e => e.Wave);

        var orderedDivisions = BuildProcessingOrder(
            activeDivisions, request.AgegroupOrder, request.DivisionOrderStrategy, excludedIds);

        // ── 5. Pre-compute per-division context (DB calls can't interleave) ──
        var divisionResults = new List<AutoBuildDivisionResult>();
        var unplacedGames = new List<UnplacedGameDto>();
        var sacrificeCounts = new Dictionary<string, (int Count, List<string> Examples)>();
        var totalPlaced = 0;
        var totalFailed = 0;

        // Holds everything needed to place games for one division
        var divContexts = new List<DivisionBuildContext>();
        var anyDeletions = false;

        // Cache agegroup-level data: DB queries + candidate generation happen once
        // per agegroup, shared across all divisions in that agegroup.
        var agContextCache = new Dictionary<Guid, AgegroupBuildContext>();

        foreach (var div in orderedDivisions)
        {
            if (excludedIds.Contains(div.DivId))
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName, DivName = div.DivName,
                    DivId = div.DivId, TeamCount = div.TeamCount,
                    GamesPlaced = 0, GamesFailed = 0, Status = "excluded"
                });
                continue;
            }

            // Handle existing games: "keep" mode preserves them; "rebuild" deletes them
            var existingGameCount = existingCounts.GetValueOrDefault(div.DivId);
            if (existingGameCount > 0)
            {
                if (keepExisting)
                {
                    divisionResults.Add(new AutoBuildDivisionResult
                    {
                        AgegroupName = div.AgegroupName, DivName = div.DivName,
                        DivId = div.DivId, TeamCount = div.TeamCount,
                        GamesPlaced = 0, GamesFailed = 0, Status = "kept"
                    });
                    continue;
                }

                await _scheduleRepo.DeleteDivisionGamesAsync(
                    div.DivId, leagueId, season, year, ct);
                await _scheduleRepo.SaveChangesAsync(ct);
                anyDeletions = true;
            }

            // ── Agegroup-level: fetch once, cache by agegroupId ──
            if (!agContextCache.TryGetValue(div.AgegroupId, out var agCtx))
            {
                var agegroup = await _agegroupRepo.GetByIdAsync(div.AgegroupId, ct);
                var agSeason = agegroup?.Season ?? season;
                var agLeagueId = agegroup?.LeagueId ?? leagueId;

                // Fetch ALL dates/fields for this agegroup (includes both
                // agegroup-level rows where DivId==null and any division-specific rows)
                var allDates = await _timeslotRepo.GetDatesAsync(div.AgegroupId, season, year, ct);
                var allFields = await _timeslotRepo.GetFieldTimeslotsAsync(div.AgegroupId, season, year, ct);

                // Extract agegroup-level defaults (DivId == null)
                var agDates = allDates.Where(d => d.DivId == null).ToList();
                var agFields = allFields.Where(f => f.DivId == null).ToList();

                // Generate default candidate slots from agegroup-level dates × fields
                List<CandidateSlot> defaultCandidates;
                var defaultWindowStart = new Dictionary<DayOfWeek, TimeSpan>();

                if (agDates.Count > 0 && agFields.Count > 0)
                {
                    var uniqueAgDates = agDates
                        .GroupBy(d => d.GDate.Date)
                        .Select(g => g.First())
                        .ToList();
                    defaultCandidates = GenerateCandidateSlots(uniqueAgDates, agFields);

                    foreach (var field in agFields)
                    {
                        if (!DateTime.TryParse(field.StartTime, out var stDt)) continue;
                        var st = stDt.TimeOfDay;
                        if (!Enum.TryParse<DayOfWeek>(field.Dow, true, out var dow)) continue;
                        if (!defaultWindowStart.TryGetValue(dow, out var existing) || st < existing)
                            defaultWindowStart[dow] = st;
                    }
                }
                else
                {
                    defaultCandidates = [];
                }

                var agWave = waveByAgId.GetValueOrDefault(div.AgegroupId, 1);

                agCtx = new AgegroupBuildContext
                {
                    Agegroup = agegroup,
                    AgSeason = agSeason,
                    AgLeagueId = agLeagueId,
                    Wave = agWave,
                    Dates = allDates,
                    Fields = allFields,
                    DefaultCandidates = defaultCandidates,
                    DefaultWindowStart = defaultWindowStart,
                    DefaultFirstGameDay = defaultCandidates.Count > 0
                        ? defaultCandidates.Min(c => c.GDate.Date)
                        : DateTime.MaxValue
                };
                agContextCache[div.AgegroupId] = agCtx;
            }

            // ── Division-level: pairings ──
            var division = await _divisionRepo.GetByIdReadOnlyAsync(div.DivId, ct);

            var teamCount = await _pairingsRepo.GetDivisionTeamCountAsync(div.DivId, jobId, ct);
            if (teamCount == 0)
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName, DivName = div.DivName,
                    DivId = div.DivId, TeamCount = 0,
                    GamesPlaced = 0, GamesFailed = 0,
                    Status = "no-teams"
                });
                continue;
            }

            var pairings = await _pairingsRepo.GetPairingsAsync(
                agCtx.AgLeagueId, agCtx.AgSeason, teamCount, ct);

            // Cap rounds by game guarantee: if provided, only include rounds up to the computed round count.
            // This ensures a 3-game guarantee in a 6-team pool only uses 3 rounds (9 games), not all 5 (15 games).
            var maxAllowedRound = ComputeRoundCount(teamCount, request.GameGuarantee);
            var rrPairings = pairings
                .Where(p => p.T1Type == "T" && p.T2Type == "T" && p.Rnd <= maxAllowedRound)
                .OrderBy(p => p.Rnd)
                .ThenBy(p => p.GameNumber)
                .ToList();

            if (rrPairings.Count == 0)
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName, DivName = div.DivName,
                    DivId = div.DivId, TeamCount = teamCount,
                    GamesPlaced = 0, GamesFailed = 0,
                    Status = "no-pairings"
                });
                continue;
            }

            // ── Effective candidates: division-specific or inherited from agegroup ──
            var divDates = agCtx.Dates.Where(d => d.DivId == div.DivId).ToList();
            var divFields = agCtx.Fields.Where(f => f.DivId == div.DivId).ToList();
            var hasDivOverrides = divDates.Count > 0 || divFields.Count > 0;

            List<CandidateSlot> candidates;
            Dictionary<DayOfWeek, TimeSpan> currentWindowStart;
            List<TimeslotFieldDto> effectiveFields;
            List<TimeslotDateDto> effectiveDates;

            if (hasDivOverrides)
            {
                // Division has its own dates and/or fields — build separate candidates
                effectiveDates = divDates.Count > 0
                    ? divDates
                    : agCtx.Dates.Where(d => d.DivId == null).ToList();
                effectiveFields = divFields.Count > 0
                    ? divFields
                    : agCtx.Fields.Where(f => f.DivId == null).ToList();

                if (effectiveDates.Count == 0 || effectiveFields.Count == 0)
                {
                    var missingCount = rrPairings.Count;
                    totalFailed += missingCount;
                    foreach (var p in rrPairings)
                    {
                        unplacedGames.Add(new UnplacedGameDto
                        {
                            AgegroupName = div.AgegroupName, DivName = div.DivName,
                            Round = p.Rnd, T1No = p.T1, T2No = p.T2,
                            Reason = "No timeslot dates or fields configured"
                        });
                    }
                    divisionResults.Add(new AutoBuildDivisionResult
                    {
                        AgegroupName = div.AgegroupName, DivName = div.DivName,
                        DivId = div.DivId, TeamCount = teamCount,
                        GamesPlaced = 0, GamesFailed = missingCount,
                        Status = "no-timeslots"
                    });
                    continue;
                }

                var uniqueDivDates = effectiveDates
                    .GroupBy(d => d.GDate.Date)
                    .Select(g => g.First())
                    .ToList();
                candidates = GenerateCandidateSlots(uniqueDivDates, effectiveFields);

                currentWindowStart = new Dictionary<DayOfWeek, TimeSpan>();
                foreach (var field in effectiveFields)
                {
                    if (!DateTime.TryParse(field.StartTime, out var stDt)) continue;
                    var st = stDt.TimeOfDay;
                    if (!Enum.TryParse<DayOfWeek>(field.Dow, true, out var dow)) continue;
                    if (!currentWindowStart.TryGetValue(dow, out var existing) || st < existing)
                        currentWindowStart[dow] = st;
                }
            }
            else
            {
                // No division overrides — inherit from agegroup
                candidates = agCtx.DefaultCandidates;
                currentWindowStart = agCtx.DefaultWindowStart;
                effectiveDates = agCtx.Dates.Where(d => d.DivId == null).ToList();
                effectiveFields = agCtx.Fields.Where(f => f.DivId == null).ToList();
            }

            if (candidates.Count == 0)
            {
                var missingCount = rrPairings.Count;
                totalFailed += missingCount;
                foreach (var p in rrPairings)
                {
                    unplacedGames.Add(new UnplacedGameDto
                    {
                        AgegroupName = div.AgegroupName, DivName = div.DivName,
                        Round = p.Rnd, T1No = p.T1, T2No = p.T2,
                        Reason = "No timeslot dates or fields configured"
                    });
                }
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName, DivName = div.DivName,
                    DivId = div.DivId, TeamCount = teamCount,
                    GamesPlaced = 0, GamesFailed = missingCount,
                    Status = "no-timeslots"
                });
                continue;
            }

            // Current year's GSI from field config
            var currentGsi = effectiveFields.Count > 0
                ? effectiveFields.Max(f => f.GamestartInterval) : 60;

            // Get or build profile for this division
            DivisionSizeProfile profile;
            if (useStrategyPath && strategyByName.TryGetValue(div.DivName, out var strategy))
            {
                // Strategy-driven: translate user choices to profile
                profile = BuildProfileFromStrategy(
                    strategy, teamCount, effectiveDates, effectiveFields, currentGsi,
                    request.GameGuarantee);
            }
            else
            {
                // Legacy TCnt-keyed path or clean sheet
                profile = GetOrBuildDefaultProfile(
                    profilesByTCnt, teamCount, effectiveDates, effectiveFields, currentGsi,
                    request.GameGuarantee);
            }

            // Build PlacementState for this division (shares global occupiedSlots + field prefs)
            var state = new PlacementState(occupiedSlots, currentWindowStart, fieldPreferences);

            // Group pairings by round (already capped by game guarantee at load time)
            var roundsByNum = rrPairings
                .GroupBy(p => p.Rnd)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.GameNumber).ToList());

            // Compute effective play days: profile's PlayDays filtered to days that
            // actually exist in this division's effective candidates.
            var candidateDays = candidates
                .Select(c => c.GDate.DayOfWeek)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var effectivePlayDays = profile.PlayDays
                .Where(d => candidateDays.Contains(d))
                .ToList();

            // If no overlap (source days don't match current at all), use candidate days
            if (effectivePlayDays.Count == 0)
                effectivePlayDays = candidateDays;

            // ── Capacity-driven: no round cap ──
            var pairingMaxRound = roundsByNum.Keys.DefaultIfEmpty(0).Max();
            var maxRound = pairingMaxRound;

            var firstGameDay = candidates.Count > 0
                ? candidates.Min(c => c.GDate.Date)
                : DateTime.MaxValue;

            divContexts.Add(new DivisionBuildContext
            {
                AgContext = agCtx,
                Div = div,
                Division = division,
                TeamCount = teamCount,
                Candidates = candidates,
                Profile = profile,
                State = state,
                CurrentWindowStart = currentWindowStart,
                RoundsByNum = roundsByNum,
                MaxRound = maxRound,
                EffectivePlayDays = effectivePlayDays,
                FirstGameDay = firstGameDay
            });
        }

        // ── 6b. Refresh occupiedSlots after deletions ──
        // The initial load (step 3) captured slots for games that were just
        // deleted in the loop above. Those "ghost" entries would incorrectly
        // block placement. Mutate the existing HashSet instance so all
        // PlacementState references see the updated set.
        // In "keep" mode with no deletions, the initial load is already correct
        // (kept divisions' slots remain, preventing double-booking).
        if (anyDeletions)
        {
            occupiedSlots.Clear();
            if (allFieldIds.Count > 0)
            {
                var freshSlots = await _scheduleRepo.GetOccupiedSlotsAsync(
                    jobId, allFieldIds, ct);
                foreach (var slot in freshSlots)
                    occupiedSlots.Add(slot);
            }
        }

        // ── 7. Chip-stack placement ──
        // Ordering: FirstGameDay → Wave → Round → Agegroup → Division → Game
        //
        // Each "chip" is one game (one pairing row).
        // FirstGameDay groups divisions by their earliest effective play date
        // (e.g., Friday divisions sort before Saturday ones). Usually matches
        // the agegroup default, but divisions with their own timeslot config
        // get their own FirstGameDay resolved at assembly time.
        // Actual game dates are determined by capacity-driven placement: the
        // scorer picks the best slot across ALL dates, filling each day until
        // full. FirstGameDay is a sort hint, not a constraint.
        // Round-before-Agegroup ensures every agegroup gets R1 placed
        // before anyone gets R2 — fair distribution of prime time slots.
        // Wave time floors separate early/late blocks on shared play days.

        // Per-division placement counters
        var divPlacedCounts = new Dictionary<Guid, int>();
        var divFailedCounts = new Dictionary<Guid, int>();
        foreach (var c in divContexts)
        {
            divPlacedCounts[c.Div.DivId] = 0;
            divFailedCounts[c.Div.DivId] = 0;
        }

        // Track which (TCnt, DayOfWeek) combos have already had a division placed.
        // Only used in legacy TCnt-keyed path — the source's StartTickOffset guides
        // the FIRST division of each TCnt to match the source's start position.
        var tcntDayFirstPlaced = new HashSet<(int TCnt, DayOfWeek Day)>();

        // Build agegroup order index for sorting within each (day, wave, round) group
        var agOrderIndex = request.AgegroupOrder
            .Select((e, i) => (e.AgegroupId, Index: i))
            .ToDictionary(x => x.AgegroupId, x => x.Index);

        // ── Build the chip stack ──
        // Each chip is ONE GAME (one pairing row). A 6-team division with
        // 5 rounds and 3 games/round produces 15 chips.
        // Stack order: FirstGameDay → Wave → Round → Agegroup → Division → Game.
        // FirstGameDay separates divisions by their earliest effective play
        // date (Friday before Saturday). Usually inherited from the agegroup,
        // but division-specific timeslot overrides produce a different value.
        // Within a day-group, Round-before-Agegroup ensures every agegroup
        // gets its R1 placed before anyone gets R2 — fair prime-slot distribution.
        // Actual game dates are capacity-driven — the scorer picks the best
        // slot across ALL dates. FirstGameDay is a sort hint, not a constraint.
        //
        // Round-level capacity check and start-time computation happen on
        // the first game of each (division, round) — tracked via roundChecked.
        var allChips = divContexts
            .SelectMany(ctx =>
                ctx.RoundsByNum
                    .Where(kvp => kvp.Key <= ctx.MaxRound)
                    .SelectMany(kvp => kvp.Value.Select((pairing, idx) => new
                    {
                        ctx.FirstGameDay,
                        Wave = ctx.AgContext.Wave,
                        RoundNum = kvp.Key,
                        GameIndex = idx,
                        GamesInRound = kvp.Value.Count,
                        AgOrder = agOrderIndex.GetValueOrDefault(ctx.Div.AgegroupId, int.MaxValue),
                        Pairing = pairing,
                        Ctx = ctx
                    })))
            .OrderBy(c => c.FirstGameDay)
            .ThenBy(c => c.Wave)
            .ThenBy(c => c.RoundNum)
            .ThenBy(c => c.AgOrder)
            .ThenBy(c => c.Ctx.Div.DivName)
            .ThenBy(c => c.GameIndex)
            .ToList();

        // ── Diagnostic: per-agegroup game + capacity summary ──
        foreach (var diagAg in divContexts
            .GroupBy(c => c.Div.AgegroupName)
            .OrderBy(g => g.Key))
        {
            var agDivs = diagAg.Count();
            var agTotalGames = diagAg.Sum(c =>
                c.RoundsByNum.Values.Sum(games => games.Count));
            var agRounds = diagAg.Max(c => c.MaxRound);
            var sampleCtx = diagAg.First();
            var agDates = sampleCtx.Candidates
                .Select(c => c.GDate.Date).Distinct().OrderBy(d => d).ToList();
            var agCapacity = sampleCtx.Candidates.Count;

            _logger.LogInformation(
                "CHIP-STACK {AgName}: {Games} games ({Rounds} rounds × {Divs} divs) | " +
                "FirstGameDay: {FirstDay} | Dates: {Dates} | Capacity: {Cap} slots",
                diagAg.Key, agTotalGames, agRounds, agDivs,
                sampleCtx.FirstGameDay.ToString("MM/dd ddd"),
                string.Join(", ", agDates.Select(d => d.ToString("MM/dd ddd"))),
                agCapacity);
        }

        _logger.LogInformation("CHIP-STACK TOTAL: {Total} chips (games) across {Ags} agegroups",
            allChips.Count, divContexts.Select(c => c.Div.AgegroupId).Distinct().Count());

        // ── Distribute chips onto the canvas ──
        // Wave floors are tracked per calendar day so Saturday's boundary
        // doesn't bleed into Sunday's placements.
        var waveFloorByDay = new Dictionary<DateTime, DateTime>();
        var waveLatestByDay = new Dictionary<DateTime, DateTime>();
        var currentWave = 0;
        var hasMultipleWaves = allChips.Select(c => c.Wave).Distinct().Count() > 1;

        // Track which (division, round) combos have been capacity-checked
        // and had their round start time computed. Only done on first game
        // of each round.
        var roundChecked = new HashSet<(Guid DivId, int Round)>();
        var skippedRounds = new HashSet<(Guid DivId, int Round)>();
        var roundStartTimes = new Dictionary<(Guid DivId, int Round), TimeSpan?>();

        foreach (var chip in allChips)
        {
            // ── Wave transition: set per-day floors from previous wave ──
            if (chip.Wave != currentWave)
            {
                if (currentWave > 0 && hasMultipleWaves && waveLatestByDay.Count > 0)
                {
                    var gsiMinutes = chip.Ctx.Profile.GsiMinutes > 0
                        ? chip.Ctx.Profile.GsiMinutes : 60;
                    foreach (var (day, latest) in waveLatestByDay)
                    {
                        waveFloorByDay[day] = latest.AddMinutes(gsiMinutes);
                    }
                }
                waveLatestByDay.Clear();
                currentWave = chip.Wave;
            }

            var ctx = chip.Ctx;
            var roundNum = chip.RoundNum;
            var pairing = chip.Pairing;
            var divRoundKey = (ctx.Div.DivId, roundNum);

            // ── If this round was already skipped (capacity), skip this game too ──
            if (skippedRounds.Contains(divRoundKey))
                continue;

            // ── First game of a new (division, round): capacity check + start time ──
            if (roundChecked.Add(divRoundKey))
            {
                // Capacity check across ALL dates — if the canvas can't fit
                // the entire round, skip it. No partial rounds.
                var openSlots = ctx.Candidates.Count(c =>
                    !ctx.State.OccupiedSlots.Contains((c.FieldId, c.GDate))
                    && !(waveFloorByDay.TryGetValue(c.GDate.Date, out var flr) && c.GDate < flr));

                if (openSlots < chip.GamesInRound)
                {
                    _logger.LogWarning(
                        "Round NOT placed: {AgName}/{DivName} R{Round} — needs {Need} slots, only {Open} open across all dates",
                        ctx.Div.AgegroupName, ctx.Div.DivName, roundNum, chip.GamesInRound, openSlots);

                    skippedRounds.Add(divRoundKey);
                    divFailedCounts[ctx.Div.DivId] += chip.GamesInRound;

                    // Report all games in this round as unplaced
                    foreach (var skipPairing in ctx.RoundsByNum[roundNum])
                    {
                        unplacedGames.Add(new UnplacedGameDto
                        {
                            AgegroupName = ctx.Div.AgegroupName,
                            DivName = ctx.Div.DivName,
                            Round = roundNum,
                            T1No = skipPairing.T1,
                            T2No = skipPairing.T2,
                            Reason = $"Canvas full: {openSlots} open slots across all dates < {chip.GamesInRound} games needed"
                        });
                    }
                    continue;
                }

                // Compute round start time hint for the scorer
                TimeSpan? roundStartTime = null;

                if (ctx.State.RoundTargetTimes.TryGetValue(divRoundKey, out var existingTarget))
                {
                    roundStartTime = existingTarget;
                }
                else
                {
                    // Find latest placed previous round for this division
                    TimeSpan? prevRoundTime = null;
                    var prevRoundNum = 0;
                    for (var prev = roundNum - 1; prev >= 1; prev--)
                    {
                        if (ctx.State.RoundTargetTimes.TryGetValue((ctx.Div.DivId, prev), out var pt))
                        {
                            prevRoundTime = pt;
                            prevRoundNum = prev;
                            break;
                        }
                    }

                    if (prevRoundTime.HasValue && ctx.Profile.GsiMinutes > 0)
                    {
                        var prevGameCount = ctx.RoundsByNum.TryGetValue(prevRoundNum, out var prevGames)
                            ? prevGames.Count : 0;

                        int minGapTicks;
                        if (ctx.Profile.RoundLayout == RoundLayout.Sequential && prevGameCount > 1)
                            minGapTicks = prevGameCount + ctx.Profile.MinTeamGapTicks - 1;
                        else
                            minGapTicks = ctx.Profile.MinTeamGapTicks;

                        var effectiveGapTicks = Math.Max(ctx.Profile.InterRoundGapTicks, minGapTicks);

                        roundStartTime = prevRoundTime.Value
                            + TimeSpan.FromMinutes(effectiveGapTicks * ctx.Profile.GsiMinutes);
                    }
                    else
                    {
                        foreach (var dow in ctx.EffectivePlayDays)
                        {
                            if (tcntDayFirstPlaced.Contains((ctx.TeamCount, dow)))
                                continue;

                            if (ctx.Profile.StartTickOffset != null
                                && ctx.Profile.StartTickOffset.TryGetValue(dow, out var tickOffset)
                                && ctx.CurrentWindowStart.TryGetValue(dow, out var winStart)
                                && ctx.Profile.GsiMinutes > 0)
                            {
                                roundStartTime = winStart + TimeSpan.FromMinutes(tickOffset * ctx.Profile.GsiMinutes);
                                break;
                            }

                            if (ctx.Profile.StartOffsetFromWindow != null
                                && ctx.Profile.StartOffsetFromWindow.TryGetValue(dow, out var offset)
                                && ctx.CurrentWindowStart.TryGetValue(dow, out var winStart2))
                            {
                                roundStartTime = winStart2 + offset;
                                break;
                            }
                        }
                    }
                }

                roundStartTimes[divRoundKey] = roundStartTime;
            }

            // ── Place this game (chip) ──
            var rst = roundStartTimes.GetValueOrDefault(divRoundKey);

            // Per-game target time: for sequential rounds, each game advances by 1 GSI tick
            var gameTargetTime = rst;
            if (ctx.Profile.RoundLayout == RoundLayout.Sequential
                && rst.HasValue && ctx.Profile.GsiMinutes > 0 && chip.GameIndex > 0)
            {
                gameTargetTime = rst.Value
                    + TimeSpan.FromMinutes(chip.GameIndex * ctx.Profile.GsiMinutes);
            }

            var game = new GameContext
            {
                Round = roundNum,
                GameNumber = pairing.GameNumber,
                T1No = pairing.T1,
                T2No = pairing.T2,
                DivId = ctx.Div.DivId,
                AgegroupId = ctx.Div.AgegroupId,
                AgegroupName = ctx.Div.AgegroupName,
                DivName = ctx.Div.DivName,
                TCnt = ctx.TeamCount,
                TargetDay = null,
                TargetTime = gameTargetTime
            };

            var best = PlacementScorer.FindBestSlot(
                ctx.Candidates, game, ctx.Profile, ctx.State,
                waveFloorByDay.Count > 0 ? waveFloorByDay : null);

            if (best == null)
            {
                var totalCandidates = ctx.Candidates.Count;
                var occupied = ctx.Candidates.Count(c =>
                    ctx.State.OccupiedSlots.Contains((c.FieldId, c.GDate)));
                var belowWaveFloor = waveFloorByDay.Count > 0
                    ? ctx.Candidates.Count(c =>
                        waveFloorByDay.TryGetValue(c.GDate.Date, out var flr) && c.GDate < flr)
                    : 0;
                var available = totalCandidates - occupied - belowWaveFloor;

                var reason = available <= 0
                    ? $"All {totalCandidates} slots occupied ({occupied} taken, {belowWaveFloor} below wave floor)"
                    : $"No feasible slot among {available} open candidates (all failed scoring)";

                divFailedCounts[ctx.Div.DivId]++;
                unplacedGames.Add(new UnplacedGameDto
                {
                    AgegroupName = ctx.Div.AgegroupName,
                    DivName = ctx.Div.DivName,
                    Round = roundNum,
                    T1No = pairing.T1,
                    T2No = pairing.T2,
                    Reason = reason
                });
                continue;
            }

            // Track penalty breakdown for sacrifice reporting
            if (best.PenaltyBreakdown.Count > 0)
            {
                foreach (var (penaltyName, penaltyValue) in best.PenaltyBreakdown)
                {
                    if (!sacrificeCounts.TryGetValue(penaltyName, out var entry))
                    {
                        entry = (0, new List<string>());
                        sacrificeCounts[penaltyName] = entry;
                    }
                    var count = entry.Count + 1;
                    var examples = entry.Examples;
                    if (examples.Count < 3)
                    {
                        examples.Add(
                            $"{ctx.Div.AgegroupName}/{ctx.Div.DivName} R{roundNum} #{pairing.T1}v#{pairing.T2}");
                    }
                    sacrificeCounts[penaltyName] = (count, examples);
                }
            }

            // Create the Schedule entity
            var scheduleGame = new Schedule
            {
                JobId = jobId,
                LeagueId = ctx.AgContext.AgLeagueId,
                Season = ctx.AgContext.AgSeason,
                Year = year,
                AgegroupId = ctx.Div.AgegroupId,
                AgegroupName = ctx.AgContext.Agegroup?.AgegroupName ?? "",
                DivId = ctx.Div.DivId,
                DivName = ctx.Division?.DivName ?? "",
                Div2Id = ctx.Div.DivId,
                FieldId = best.Slot.FieldId,
                FName = best.Slot.FieldName,
                GDate = best.Slot.GDate,
                GNo = pairing.GameNumber,
                GStatusCode = 1,
                Rnd = (byte)pairing.Rnd,
                T1No = pairing.T1,
                T1Type = pairing.T1Type,
                T2No = (byte)pairing.T2,
                T2Type = pairing.T2Type,
                T1Ann = pairing.T1Annotation,
                T1CalcType = pairing.T1CalcType,
                T1GnoRef = pairing.T1GnoRef,
                T2Ann = pairing.T2Annotation,
                T2CalcType = pairing.T2CalcType,
                T2GnoRef = pairing.T2GnoRef,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            };

            _scheduleRepo.AddGame(scheduleGame);
            ctx.State.RecordPlacement(best.Slot, game);
            divPlacedCounts[ctx.Div.DivId]++;

            // Track latest placement per day for wave floor computation
            var placedDay = best.Slot.GDate.Date;
            if (!waveLatestByDay.TryGetValue(placedDay, out var dayLatest)
                || best.Slot.GDate > dayLatest)
            {
                waveLatestByDay[placedDay] = best.Slot.GDate;
            }

            // Mark this TCnt+Day as having its first division placed.
            var placedDow = best.Slot.GDate.DayOfWeek;
            tcntDayFirstPlaced.Add((ctx.TeamCount, placedDow));
        }

        // ── 8. Bulk save and finalize per division ──
        // Save all placed games in one batch
        if (divPlacedCounts.Values.Any(c => c > 0))
            await _scheduleRepo.SaveChangesAsync(ct);

        // Resolve team names per division
        foreach (var ctx in divContexts)
        {
            if (divPlacedCounts[ctx.Div.DivId] > 0)
            {
                await _scheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(
                    ctx.Div.DivId, jobId, ct);
            }

            var placed = divPlacedCounts[ctx.Div.DivId];
            var failed = divFailedCounts[ctx.Div.DivId];
            divisionResults.Add(new AutoBuildDivisionResult
            {
                AgegroupName = ctx.Div.AgegroupName,
                DivName = ctx.Div.DivName,
                DivId = ctx.Div.DivId,
                TeamCount = ctx.TeamCount,
                GamesPlaced = placed,
                GamesFailed = failed,
                Status = placed > 0 ? "placed" : "no-slots"
            });

            totalPlaced += placed;
            totalFailed += failed;
        }

        // ── 8. Build sacrifice log from penalty breakdown ──
        var penaltyImpactDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wrong-day"] = "Games placed on a different day than the source schedule used.",
            ["team-span"] = "A team's first-to-last game span exceeds the source pattern — longer wait at the field.",
            ["team-gap"] = "Teams have games closer together than the source schedule — potential back-to-back.",
            ["target-time"] = "Games placed at a different time than the source pattern — drift from expected position.",
            ["round-layout"] = "Horizontal round couldn't place all games at the same time — insufficient fields.",
            ["field-balance"] = "Some teams play on the same field more often than ideal — field rotation imbalance.",
            ["field-avoid"] = "Games placed on a field marked as 'Avoid' — no other slots available without worse trade-offs."
        };

        var sacrificeLog = sacrificeCounts
            .Select(kvp => new ConstraintSacrificeDto
            {
                ConstraintName = kvp.Key,
                ViolationCount = kvp.Value.Count,
                ExampleGames = kvp.Value.Examples,
                ImpactDescription = penaltyImpactDescriptions.GetValueOrDefault(kvp.Key, "")
            })
            .OrderByDescending(s => s.ViolationCount)
            .ToList();

        var kept = divisionResults.Count(r => r.Status == "kept");
        var keptGames = existingCounts
            .Where(kvp => divisionResults.Any(r => r.DivId == kvp.Key && r.Status == "kept"))
            .Sum(kvp => kvp.Value);
        var scheduled = divisionResults.Count(r =>
            r.Status != "excluded" && r.Status != "no-teams"
            && r.Status != "no-pairings" && r.Status != "kept");
        var skipped = divisionResults.Count - scheduled - kept;

        // ── 9. Save strategy profiles to DB if requested ──
        if (useStrategyPath && request.SaveProfiles && totalPlaced > 0)
        {
            var profilesToSave = request.DivisionStrategies!
                .Select(s => new Domain.Entities.DivisionScheduleProfile
                {
                    ProfileId = Guid.NewGuid(),
                    JobId = jobId,
                    DivisionName = s.DivisionName,
                    Placement = (byte)s.Placement,
                    GapPattern = (byte)s.GapPattern
                })
                .ToList();

            await _divisionProfileRepo.UpsertBatchAsync(jobId, profilesToSave, ct);
            await _divisionProfileRepo.SaveChangesAsync(ct);

            _logger.LogInformation(
                "AutoBuild: Saved {Count} strategy profiles for Job={JobId}",
                profilesToSave.Count, jobId);
        }

        _logger.LogInformation(
            "AutoBuild: Job={JobId}, Divisions={Total}, Scheduled={Scheduled}, Kept={Kept}, " +
            "GamesPlaced={Placed}, GamesFailed={Failed}, Sacrifices={SacrificeCount}",
            jobId, orderedDivisions.Count, scheduled, kept, totalPlaced, totalFailed, sacrificeLog.Count);

        return new AutoBuildResult
        {
            TotalDivisions = orderedDivisions.Count,
            DivisionsScheduled = scheduled,
            DivisionsSkipped = skipped,
            DivisionsKept = kept,
            ExistingGamesKept = keptGames,
            TotalGamesPlaced = totalPlaced,
            GamesFailedToPlace = totalFailed,
            DivisionResults = divisionResults,
            UnplacedGames = unplacedGames,
            SacrificeLog = sacrificeLog
        };
    }

    // ══════════════════════════════════════════════════════════
    // Load Strategy Profiles (three-layer resolution)
    // ══════════════════════════════════════════════════════════

    public async Task<DivisionStrategyProfileResponse> LoadStrategyProfilesAsync(
        Guid jobId, Guid? sourceJobId, CancellationToken ct = default)
    {
        // Layer 1: Check for saved profiles in DB
        var saved = await _divisionProfileRepo.GetByJobIdAsync(jobId, ct);
        if (saved.Count > 0)
        {
            // Cross-reference saved profiles against current active divisions
            // to detect orphans (divisions renamed via sync pools, teams moved, etc.)
            var activeDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
            var activeDivNames = FilterSchedulableDivisions(activeDivisions)
                .Select(d => d.DivName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var validSaved = saved
                .Where(p => activeDivNames.Contains(p.DivisionName))
                .ToList();
            var orphanedNames = saved
                .Where(p => !activeDivNames.Contains(p.DivisionName))
                .Select(p => p.DivisionName)
                .ToList();

            // Clean up orphaned rows from DB so they don't accumulate
            if (orphanedNames.Count > 0)
            {
                await _divisionProfileRepo.DeleteOrphansByNamesAsync(jobId, orphanedNames, ct);
            }

            if (validSaved.Count > 0)
            {
                // Some saved profiles survived — use them, fill gaps with defaults
                var missingDivNames = activeDivNames
                    .Where(n => !validSaved.Any(p =>
                        string.Equals(p.DivisionName, n, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(n => n)
                    .ToList();

                var strategies = validSaved.Select(p => new DivisionStrategyEntry
                {
                    DivisionName = p.DivisionName,
                    Placement = p.Placement,
                    GapPattern = p.GapPattern
                }).ToList();

                // Fill gaps for new division names (post-sync) with defaults
                foreach (var name in missingDivNames)
                {
                    strategies.Add(new DivisionStrategyEntry
                    {
                        DivisionName = name,
                        Placement = 0,  // Horizontal
                        GapPattern = 1  // OneOnOneOff
                    });
                }

                // Compute disconnects if we have a source job to compare against
                List<PreFlightDisconnect>? disconnects = null;
                if (sourceJobId.HasValue)
                {
                    var patterns = await _autoBuildRepo.ExtractPatternAsync(sourceJobId.Value, ct);
                    if (patterns.Count > 0)
                    {
                        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(sourceJobId.Value, ct);
                        var sourceWindow = await GetSourceTimeslotWindowAsync(sourceJobId.Value, ct);
                        var currentDivCountByTCnt = FilterSchedulableDivisions(activeDivisions)
                            .GroupBy(d => d.TeamCount)
                            .ToDictionary(g => g.Key, g => g.Count());
                        var profilesByTCnt = AttributeExtractor.ExtractProfiles(
                            patterns, sourceDivisions, currentDivCountByTCnt, sourceWindow);

                        // Translate source field names → current names before disconnect check
                        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);
                        var fieldMap = await BuildFieldNameMapAsync(patterns, leagueId, season, ct);
                        if (fieldMap.Count > 0)
                        {
                            profilesByTCnt = profilesByTCnt.ToDictionary(
                                kvp => kvp.Key,
                                kvp => ApplyFieldNameMap(kvp.Value, fieldMap));
                        }

                        var disc = await CheckDisconnectsAsync(jobId, profilesByTCnt, sourceJobId.Value, ct);
                        if (disc.Count > 0) disconnects = disc;
                    }
                }

                return new DivisionStrategyProfileResponse
                {
                    Strategies = strategies.OrderBy(s => s.DivisionName).ToList(),
                    Source = orphanedNames.Count > 0 ? "saved-cleaned" : "saved",
                    Disconnects = disconnects
                };
            }
            // All saved profiles were orphaned — fall through to Layer 2/3
        }

        // Layer 2: Infer from source job via AttributeExtractor
        if (sourceJobId.HasValue)
        {
            var patterns = await _autoBuildRepo.ExtractPatternAsync(sourceJobId.Value, ct);
            if (patterns.Count > 0)
            {
                var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(
                    sourceJobId.Value, ct);
                var sourceWindow = await GetSourceTimeslotWindowAsync(sourceJobId.Value, ct);
                var currentDivisions = FilterSchedulableDivisions(
                    await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct));
                var currentDivCountByTCnt = currentDivisions
                    .GroupBy(d => d.TeamCount)
                    .ToDictionary(g => g.Key, g => g.Count());

                var profilesByTCnt = AttributeExtractor.ExtractProfiles(
                    patterns, sourceDivisions, currentDivCountByTCnt, sourceWindow);

                // Get distinct division names and map to strategy entries via TCnt
                var divNameToTCnt = currentDivisions
                    .GroupBy(d => d.DivName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.First().TeamCount,
                        StringComparer.OrdinalIgnoreCase);

                var strategies = new List<DivisionStrategyEntry>();
                foreach (var (divName, tCnt) in divNameToTCnt)
                {
                    if (profilesByTCnt.TryGetValue(tCnt, out var profile))
                    {
                        strategies.Add(new DivisionStrategyEntry
                        {
                            DivisionName = divName,
                            Placement = profile.RoundLayout == RoundLayout.Sequential ? 1 : 0,
                            GapPattern = Math.Max(0, profile.MinTeamGapTicks - 1)
                        });
                    }
                    else
                    {
                        // Default: Horizontal + OneOnOneOff
                        strategies.Add(new DivisionStrategyEntry
                        {
                            DivisionName = divName,
                            Placement = 0,
                            GapPattern = 1
                        });
                    }
                }

                // Translate source field names → current names before disconnect check
                var (leagueId2, season2, _) = await _contextResolver.ResolveAsync(jobId, ct);
                var fieldMap = await BuildFieldNameMapAsync(patterns, leagueId2, season2, ct);
                if (fieldMap.Count > 0)
                {
                    profilesByTCnt = profilesByTCnt.ToDictionary(
                        kvp => kvp.Key,
                        kvp => ApplyFieldNameMap(kvp.Value, fieldMap));
                }

                var sourceJobName = await _autoBuildRepo.GetJobNameAsync(sourceJobId.Value, ct);
                var disconnects = await CheckDisconnectsAsync(jobId, profilesByTCnt, sourceJobId.Value, ct);

                return new DivisionStrategyProfileResponse
                {
                    Strategies = strategies.OrderBy(s => s.DivisionName).ToList(),
                    Source = "inferred",
                    InferredFromJobId = sourceJobId.Value,
                    InferredFromJobName = sourceJobName ?? "Unknown",
                    Disconnects = disconnects.Count > 0 ? disconnects : null
                };
            }
        }

        // Layer 3: Defaults from current division names
        var allDivisions = FilterSchedulableDivisions(
            await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct));
        var distinctDivNames = allDivisions
            .Select(d => d.DivName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        return new DivisionStrategyProfileResponse
        {
            Strategies = distinctDivNames.Select(n => new DivisionStrategyEntry
            {
                DivisionName = n,
                Placement = 0,  // Horizontal
                GapPattern = 1  // OneOnOneOff
            }).ToList(),
            Source = "defaults"
        };
    }

    // ══════════════════════════════════════════════════════════
    // Private: Scheduling Filters
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Filter division summaries to only schedulable divisions:
    /// excludes Waitlist/Dropped agegroups, Unassigned divisions, and Dropped divisions.
    /// </summary>
    private static List<CurrentDivisionSummary> FilterSchedulableDivisions(
        List<CurrentDivisionSummary> divisions)
    {
        return divisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.Contains("Waitlist", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.Contains("Dropped", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase)
                     && !d.DivName.Contains("Unassigned", StringComparison.OrdinalIgnoreCase)
                     && !d.DivName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ══════════════════════════════════════════════════════════
    // Private: Game Guarantee Helpers
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Compute the number of rounds needed for a given pool size and game guarantee.
    /// For even TCnt: guarantee rounds = gameGuarantee (each round, every team plays).
    /// For odd TCnt: one team sits each round, so need gameGuarantee + 1 rounds
    /// to ensure every team plays at least gameGuarantee games.
    /// Returns full-RR round count when gameGuarantee is null/0 or >= full-RR.
    /// </summary>
    internal static int ComputeRoundCount(int teamCount, int? gameGuarantee)
    {
        var fullRr = teamCount % 2 == 0 ? teamCount - 1 : teamCount;
        if (gameGuarantee is null or 0)
            return fullRr;

        // For even team counts: every team plays every round, so rounds = gameGuarantee
        // For odd team counts: one team has a bye each round, so need gameGuarantee + 1
        // to guarantee every team gets at least gameGuarantee games.
        // But never exceed full round-robin.
        var needed = teamCount % 2 == 0
            ? gameGuarantee.Value
            : gameGuarantee.Value + 1;

        return Math.Clamp(needed, 1, fullRr);
    }

    /// <summary>
    /// Compute the expected game count for a pool given team count and game guarantee.
    /// Uses the round count from ComputeRoundCount × games per round (teamCount/2).
    /// </summary>
    internal static int ComputeExpectedGames(int teamCount, int? gameGuarantee)
    {
        if (teamCount < 2) return 0;
        var fullRr = teamCount * (teamCount - 1) / 2;
        if (gameGuarantee is null or 0)
            return fullRr;

        var rounds = ComputeRoundCount(teamCount, gameGuarantee);
        var gamesPerRound = teamCount / 2;
        var expected = rounds * gamesPerRound;

        // Never exceed full round-robin
        return Math.Min(expected, fullRr);
    }

    // ══════════════════════════════════════════════════════════
    // Private: Agegroup Build Context
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Agegroup-level data cached once per agegroupId. Eliminates redundant DB
    /// queries and candidate-slot generation when multiple divisions share an
    /// agegroup. Built during the async first pass (step 5).
    /// </summary>
    private sealed class AgegroupBuildContext
    {
        public required Agegroups? Agegroup { get; init; }
        public required string AgSeason { get; init; }
        public required Guid AgLeagueId { get; init; }

        /// <summary>
        /// Wave group (1-based). Engine completes all Wave 1 agegroups on a day
        /// before starting Wave 2 on the same day.
        /// </summary>
        public required int Wave { get; init; }

        /// <summary>Agegroup-level dates (DivId == null).</summary>
        public required List<TimeslotDateDto> Dates { get; init; }

        /// <summary>Agegroup-level field timeslots (DivId == null).</summary>
        public required List<TimeslotFieldDto> Fields { get; init; }

        /// <summary>
        /// Default candidate slots generated from agegroup-level dates × fields.
        /// Shared across all divisions unless a division has its own timeslots.
        /// </summary>
        public required List<CandidateSlot> DefaultCandidates { get; init; }

        /// <summary>
        /// Current job's timeslot window start per DOW (earliest configured field
        /// start time). Derived from agegroup-level fields.
        /// </summary>
        public required Dictionary<DayOfWeek, TimeSpan> DefaultWindowStart { get; init; }

        /// <summary>
        /// First game day derived from agegroup-level candidates.
        /// Divisions inherit this unless they have their own timeslots.
        /// </summary>
        public required DateTime DefaultFirstGameDay { get; init; }
    }

    // ══════════════════════════════════════════════════════════
    // Private: Division Build Context
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Pre-computed context for one division, built during the async first pass.
    /// References its parent AgegroupBuildContext for shared agegroup-level data.
    /// Division-specific timeslot overrides (if any) produce different effective
    /// candidates and first game day than the agegroup defaults.
    /// </summary>
    private sealed class DivisionBuildContext
    {
        /// <summary>Shared agegroup-level data (dates, fields, candidates, wave, etc.).</summary>
        public required AgegroupBuildContext AgContext { get; init; }

        public required CurrentDivisionSummary Div { get; init; }
        public Divisions? Division { get; init; }
        public required int TeamCount { get; init; }
        public required DivisionSizeProfile Profile { get; init; }
        public required PlacementState State { get; init; }
        public required Dictionary<int, List<PairingsLeagueSeason>> RoundsByNum { get; init; }
        public required int MaxRound { get; init; }

        /// <summary>
        /// Effective candidate slots for this division. Points to AgContext.DefaultCandidates
        /// when division has no timeslot overrides; otherwise a division-specific list.
        /// </summary>
        public required List<CandidateSlot> Candidates { get; init; }

        /// <summary>
        /// Effective window start per DOW. Points to AgContext.DefaultWindowStart
        /// when division has no field overrides; otherwise division-specific.
        /// </summary>
        public required Dictionary<DayOfWeek, TimeSpan> CurrentWindowStart { get; init; }

        /// <summary>
        /// Profile PlayDays filtered to only include days that actually exist in this
        /// division's effective candidate slots. Prevents false wrong-day penalties
        /// when profiles are shared across agegroups that play on different days.
        /// </summary>
        public required List<DayOfWeek> EffectivePlayDays { get; init; }

        /// <summary>
        /// Earliest effective candidate date for this division. Used as the primary
        /// chip-stack sort key so Friday-playing divisions sort before Saturday-playing
        /// ones. Inherits from AgContext.DefaultFirstGameDay when division has no
        /// timeslot overrides. NOT a per-game assignment — actual game dates are
        /// determined by capacity-driven placement.
        /// </summary>
        public required DateTime FirstGameDay { get; init; }
    }

    // ══════════════════════════════════════════════════════════
    // Private: Processing Order
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Build the ordered list of divisions to process based on agegroup order and division strategy.
    /// </summary>
    private static List<CurrentDivisionSummary> BuildProcessingOrder(
        List<CurrentDivisionSummary> activeDivisions,
        List<AgegroupBuildEntry> agegroupOrder,
        string divisionOrderStrategy,
        HashSet<Guid> excludedIds)
    {
        // Group divisions by agegroup
        var divsByAgegroup = activeDivisions
            .GroupBy(d => d.AgegroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<CurrentDivisionSummary>();

        // Process agegroups in specified order
        foreach (var entry in agegroupOrder)
        {
            if (!divsByAgegroup.TryGetValue(entry.AgegroupId, out var divisions))
                continue;

            // Sort divisions within agegroup by strategy
            var ordered = divisionOrderStrategy switch
            {
                "odd-first" => divisions
                    .OrderByDescending(d => d.TeamCount % 2 != 0) // odd first
                    .ThenBy(d => d.DivName)
                    .ToList(),
                "custom" => divisions, // already in user-defined order
                _ => divisions // "alpha"
                    .OrderBy(d => d.DivName)
                    .ToList()
            };

            result.AddRange(ordered);
        }

        // Add any agegroups not in the specified order (safety net)
        var processedAgIds = agegroupOrder.Select(e => e.AgegroupId).ToHashSet();
        foreach (var (agId, divisions) in divsByAgegroup)
        {
            if (processedAgIds.Contains(agId))
                continue;

            result.AddRange(divisions.OrderBy(d => d.DivName));
        }

        return result;
    }

    // ══════════════════════════════════════════════════════════
    // Private: Candidate Slot Generation
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Generate all possible (field, datetime) candidates from timeslot config.
    /// Walks dates × fields × game intervals to produce every schedulable slot.
    /// </summary>
    private static List<CandidateSlot> GenerateCandidateSlots(
        List<TimeslotDateDto> dates,
        List<TimeslotFieldDto> fields)
    {
        var candidates = new List<CandidateSlot>();

        foreach (var date in dates.OrderBy(d => d.GDate))
        {
            var dow = date.GDate.DayOfWeek.ToString();
            var dowFields = fields
                .Where(f => f.Dow.Equals(dow, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.FieldName)
                .ToList();

            foreach (var field in dowFields)
            {
                // Parse start time (supports "08:00 AM" format)
                if (!DateTime.TryParse(field.StartTime, out var startDt))
                    continue;
                var startTime = startDt.TimeOfDay;

                var intervalMinutes = field.GamestartInterval;
                if (intervalMinutes <= 0) intervalMinutes = 60; // safety

                // Generate slots from start time through max games
                for (var g = 0; g < field.MaxGamesPerField; g++)
                {
                    var gameTime = startTime + TimeSpan.FromMinutes(g * intervalMinutes);
                    var gDate = date.GDate.Date + gameTime;

                    candidates.Add(new CandidateSlot
                    {
                        FieldId = field.FieldId,
                        FieldName = field.FieldName,
                        GDate = gDate
                    });
                }
            }
        }

        // Time-first ordering: group all fields at the same time together.
        // The scorer short-circuits on the first zero-penalty candidate, so
        // iteration order determines which field gets picked when multiple
        // candidates tie at penalty 0. Field-first ordering (the loop above)
        // causes a "Field_A cascade" where every division grabs the next
        // available slot on the first field alphabetically, packing one field
        // forward through the day while leaving other fields empty. By the
        // 5th+ division the time range is exhausted and R2/R3 fail.
        //
        // Time-first ordering makes divisions spread across fields at the
        // same time position: Div1 gets 08:00-A/B, Div2 gets 08:00-C/D, etc.
        // All 15 fields participate, and every division has room for R2/R3.
        return candidates
            .OrderBy(c => c.GDate)
            .ThenBy(c => c.FieldName)
            .ToList();
    }

    // ══════════════════════════════════════════════════════════
    // Private: Default Profile for Clean Sheet
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Get a profile from extracted profiles, or build a default from timeslot config.
    /// Clean sheet mode: no prior year data, so derive sensible defaults.
    /// </summary>
    private static DivisionSizeProfile GetOrBuildDefaultProfile(
        Dictionary<int, DivisionSizeProfile> profilesByTCnt,
        int teamCount,
        List<TimeslotDateDto> dates,
        List<TimeslotFieldDto> fields,
        int currentGsi,
        int? requestGameGuarantee = null)
    {
        if (profilesByTCnt.TryGetValue(teamCount, out var profile))
            return profile;

        // Build default profile from timeslot config
        var playDays = dates
            .Select(d => d.GDate.DayOfWeek)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var fieldBand = fields
            .Select(f => f.FieldName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var timeRangeAbsolute = new Dictionary<DayOfWeek, TimeRangeDto>();
        foreach (var day in playDays)
        {
            var dayFields = fields
                .Where(f => f.Dow.Equals(day.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dayFields.Count == 0) continue;

            var startTimes = dayFields
                .Select(f => DateTime.TryParse(f.StartTime, out var dt) ? dt.TimeOfDay : TimeSpan.Zero)
                .Where(t => t > TimeSpan.Zero)
                .ToList();
            if (startTimes.Count == 0) continue;

            var minStart = startTimes.Min();
            var maxInterval = dayFields.Max(f => f.GamestartInterval);
            var maxGames = dayFields.Max(f => f.MaxGamesPerField);
            var maxEnd = minStart + TimeSpan.FromMinutes(maxInterval * maxGames);

            timeRangeAbsolute[day] = new TimeRangeDto { Start = minStart, End = maxEnd };
        }

        // Round count: capped by game guarantee when provided
        var roundCount = ComputeRoundCount(teamCount, requestGameGuarantee);
        var gameGuarantee = requestGameGuarantee is > 0
            ? Math.Min(requestGameGuarantee.Value, teamCount - 1)
            : teamCount - 1;

        // Default interval from field config
        var gsi = currentGsi > 0 ? currentGsi : 60;
        var defaultInterval = TimeSpan.FromMinutes(gsi);

        // Default rounds per day: distribute evenly
        var roundsPerDay = new Dictionary<DayOfWeek, int>();
        if (playDays.Count > 0)
        {
            var roundsEach = roundCount / playDays.Count;
            var remainder = roundCount % playDays.Count;
            for (var i = 0; i < playDays.Count; i++)
            {
                roundsPerDay[playDays[i]] = roundsEach + (i < remainder ? 1 : 0);
            }
        }

        // Determine default round layout: if enough fields for all games in a round, horizontal
        var gamesPerRound = teamCount / 2; // round-robin: half the teams play per round
        var fieldCount = fieldBand.Count;
        var defaultLayout = fieldCount >= gamesPerRound
            ? RoundLayout.Horizontal
            : RoundLayout.Sequential;

        // Default inter-round gap: for sequential = gamesPerRound ticks, for horizontal = 1 tick
        var defaultInterRoundGapTicks = defaultLayout == RoundLayout.Horizontal
            ? 1
            : gamesPerRound;

        var defaultProfile = new DivisionSizeProfile
        {
            TCnt = teamCount,
            DivisionCount = 1,
            PlayDays = playDays,
            TimeRangeAbsolute = timeRangeAbsolute,
            FieldBand = fieldBand,
            RoundCount = roundCount,
            GameGuarantee = gameGuarantee,
            PlacementShapePerRound = new Dictionary<int, RoundShapeDto>(),
            OnsiteIntervalPerDay = timeRangeAbsolute.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.End - kvp.Value.Start),
            FieldDesirability = new Dictionary<string, FieldUsageDto>(),
            RoundsPerDay = roundsPerDay,
            ExtraRoundDay = null,
            InterRoundInterval = defaultInterval,
            // Tick-based defaults for clean sheet mode
            GsiMinutes = gsi,
            RoundLayout = defaultLayout,
            StartTickOffset = playDays.ToDictionary(d => d, _ => 0),
            InterRoundGapTicks = defaultInterRoundGapTicks,
            MinTeamGapTicks = 2, // No BTBs by default
            FieldFairness = FieldFairness.Democratic
        };

        // Cache for subsequent divisions with same TCnt
        profilesByTCnt[teamCount] = defaultProfile;
        return defaultProfile;
    }

    // ══════════════════════════════════════════════════════════
    // Private: Build Profile from Strategy Entry
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Translate a user-chosen DivisionStrategyEntry into a DivisionSizeProfile.
    /// Used by the strategy-driven code path (replaces source-job extraction).
    /// </summary>
    private static DivisionSizeProfile BuildProfileFromStrategy(
        DivisionStrategyEntry strategy,
        int teamCount,
        List<TimeslotDateDto> dates,
        List<TimeslotFieldDto> fields,
        int currentGsi,
        int? requestGameGuarantee = null)
    {
        var playDays = dates
            .Select(d => d.GDate.DayOfWeek)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var fieldBand = fields
            .Select(f => f.FieldName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var timeRangeAbsolute = new Dictionary<DayOfWeek, TimeRangeDto>();
        foreach (var day in playDays)
        {
            var dayFields = fields
                .Where(f => f.Dow.Equals(day.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dayFields.Count == 0) continue;

            var startTimes = dayFields
                .Select(f => DateTime.TryParse(f.StartTime, out var dt) ? dt.TimeOfDay : TimeSpan.Zero)
                .Where(t => t > TimeSpan.Zero)
                .ToList();
            if (startTimes.Count == 0) continue;

            var minStart = startTimes.Min();
            var maxInterval = dayFields.Max(f => f.GamestartInterval);
            var maxGames = dayFields.Max(f => f.MaxGamesPerField);
            var maxEnd = minStart + TimeSpan.FromMinutes(maxInterval * maxGames);

            timeRangeAbsolute[day] = new TimeRangeDto { Start = minStart, End = maxEnd };
        }

        // Round count: capped by game guarantee when provided
        var roundCount = ComputeRoundCount(teamCount, requestGameGuarantee);
        var gameGuarantee = requestGameGuarantee is > 0
            ? Math.Min(requestGameGuarantee.Value, teamCount - 1)
            : teamCount - 1;
        var gsi = currentGsi > 0 ? currentGsi : 60;
        var gamesPerRound = teamCount / 2;

        // Translate strategy choices to profile properties
        var roundLayout = strategy.Placement == 1
            ? RoundLayout.Sequential
            : RoundLayout.Horizontal;

        // GapPattern → MinTeamGapTicks (+1 mapping)
        var minTeamGapTicks = strategy.GapPattern + 1;

        // InterRoundGapTicks derived from layout and gap
        var interRoundGapTicks = roundLayout == RoundLayout.Horizontal
            ? minTeamGapTicks
            : gamesPerRound + minTeamGapTicks - 1;

        // Distribute rounds across play days
        var roundsPerDay = new Dictionary<DayOfWeek, int>();
        if (playDays.Count > 0)
        {
            var roundsEach = roundCount / playDays.Count;
            var remainder = roundCount % playDays.Count;
            for (var i = 0; i < playDays.Count; i++)
            {
                roundsPerDay[playDays[i]] = roundsEach + (i < remainder ? 1 : 0);
            }
        }

        return new DivisionSizeProfile
        {
            TCnt = teamCount,
            DivisionCount = 1,
            PlayDays = playDays,
            TimeRangeAbsolute = timeRangeAbsolute,
            FieldBand = fieldBand,
            RoundCount = roundCount,
            GameGuarantee = gameGuarantee,
            PlacementShapePerRound = new Dictionary<int, RoundShapeDto>(),
            OnsiteIntervalPerDay = timeRangeAbsolute.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.End - kvp.Value.Start),
            FieldDesirability = new Dictionary<string, FieldUsageDto>(),
            RoundsPerDay = roundsPerDay,
            ExtraRoundDay = null,
            InterRoundInterval = TimeSpan.FromMinutes(gsi),
            GsiMinutes = gsi,
            RoundLayout = roundLayout,
            StartTickOffset = playDays.ToDictionary(d => d, _ => 0),
            InterRoundGapTicks = interRoundGapTicks,
            MinTeamGapTicks = minTeamGapTicks,
            FieldFairness = FieldFairness.Democratic
        };
    }

    // ══════════════════════════════════════════════════════════
    // Private: Source Timeslot Window
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Resolve the source job's timeslot window: the earliest configured field start time
    /// per day of week, across all agegroups. Uses the same pattern as ScheduleDivisionService
    /// to materialize timeslot data (contextResolver → agegroups → fieldTimeslots).
    /// </summary>
    private async Task<Dictionary<DayOfWeek, TimeSpan>> GetSourceTimeslotWindowAsync(
        Guid sourceJobId, CancellationToken ct)
    {
        var (srcLeagueId, srcSeason, srcYear) = await _contextResolver.ResolveAsync(sourceJobId, ct);
        var srcAgegroups = await _agegroupRepo.GetByLeagueIdAsync(srcLeagueId, ct);

        var windowStartPerDow = new Dictionary<DayOfWeek, TimeSpan>();

        foreach (var ag in srcAgegroups)
        {
            var fields = await _timeslotRepo.GetFieldTimeslotsAsync(ag.AgegroupId, srcSeason, srcYear, ct);

            foreach (var field in fields)
            {
                if (!DateTime.TryParse(field.StartTime, out var startDt))
                    continue;
                var startTime = startDt.TimeOfDay;

                if (!Enum.TryParse<DayOfWeek>(field.Dow, true, out var dow))
                    continue;

                if (!windowStartPerDow.TryGetValue(dow, out var existing) || startTime < existing)
                    windowStartPerDow[dow] = startTime;
            }
        }

        return windowStartPerDow;
    }

    // ══════════════════════════════════════════════════════════
    // Private: Pre-Flight Disconnects
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Compare source-extracted profiles against the current job's timeslot canvas.
    /// Surfaces any mismatches (missing fields, shifted time windows, GSI changes)
    /// so the scheduler sees exactly where the engine may deviate from last year's pattern.
    /// </summary>
    private async Task<List<PreFlightDisconnect>> CheckDisconnectsAsync(
        Guid jobId, Dictionary<int, DivisionSizeProfile> profiles,
        Guid sourceJobId, CancellationToken ct)
    {
        var disconnects = new List<PreFlightDisconnect>();
        if (profiles.Count == 0)
            return disconnects;

        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Gather current timeslot canvas: fields + start times + GSI across all agegroups
        var currentAgegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, ct);
        var currentFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentWindowStart = new Dictionary<DayOfWeek, TimeSpan>();
        var currentGsiValues = new HashSet<int>();

        foreach (var ag in currentAgegroups)
        {
            var fields = await _timeslotRepo.GetFieldTimeslotsAsync(ag.AgegroupId, season, year, ct);
            foreach (var field in fields)
            {
                currentFieldNames.Add(field.FieldName);
                currentGsiValues.Add(field.GamestartInterval);

                if (!DateTime.TryParse(field.StartTime, out var stDt)) continue;
                var st = stDt.TimeOfDay;
                if (!Enum.TryParse<DayOfWeek>(field.Dow, true, out var dow)) continue;
                if (!currentWindowStart.TryGetValue(dow, out var existing) || st < existing)
                    currentWindowStart[dow] = st;
            }
        }

        // ── Check 1: Field availability ──
        // Collect all field names referenced across all profiles (already mapped to current names)
        var profileFieldNames = profiles.Values
            .SelectMany(p => p.FieldBand)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingFields = profileFieldNames
            .Where(f => !currentFieldNames.Contains(f))
            .ToList();

        foreach (var field in missingFields)
        {
            disconnects.Add(new PreFlightDisconnect
            {
                Category = "field",
                Description = $"Source used \"{field}\" but it's not in this year's timeslots"
            });
        }

        // ── Check 2: Time range — earliest source start vs earliest current start per DOW ──
        foreach (var profile in profiles.Values)
        {
            foreach (var (dow, range) in profile.TimeRangeAbsolute)
            {
                if (!currentWindowStart.TryGetValue(dow, out var currentEarliest))
                    continue; // DOW not configured — will be caught by day ordinal mapping

                if (range.Start < currentEarliest)
                {
                    var sourceTime = DateTime.Today.Add(range.Start).ToString("h:mm tt");
                    var currentTime = DateTime.Today.Add(currentEarliest).ToString("h:mm tt");
                    disconnects.Add(new PreFlightDisconnect
                    {
                        Category = "time",
                        Description = $"Source {profile.TCnt}-team divisions started at {sourceTime} on {dow}s " +
                                      $"but earliest available slot is {currentTime}"
                    });
                }
            }
        }

        // ── Check 3: GSI change (only if different between source and current) ──
        var (srcLeagueId, srcSeason, srcYear) = await _contextResolver.ResolveAsync(sourceJobId, ct);
        var srcAgegroups = await _agegroupRepo.GetByLeagueIdAsync(srcLeagueId, ct);
        var sourceGsiValues = new HashSet<int>();

        foreach (var ag in srcAgegroups)
        {
            var fields = await _timeslotRepo.GetFieldTimeslotsAsync(ag.AgegroupId, srcSeason, srcYear, ct);
            foreach (var field in fields)
                sourceGsiValues.Add(field.GamestartInterval);
        }

        // Compare: if any source GSI differs from ALL current GSIs, flag it
        var distinctSourceGsi = sourceGsiValues.OrderBy(g => g).ToList();
        var distinctCurrentGsi = currentGsiValues.OrderBy(g => g).ToList();

        if (distinctSourceGsi.Count > 0 && distinctCurrentGsi.Count > 0
            && !distinctSourceGsi.SequenceEqual(distinctCurrentGsi))
        {
            var srcGsiStr = string.Join("/", distinctSourceGsi.Select(g => $"{g} min"));
            var curGsiStr = string.Join("/", distinctCurrentGsi.Select(g => $"{g} min"));
            disconnects.Add(new PreFlightDisconnect
            {
                Category = "interval",
                Description = $"Game interval changed from {srcGsiStr} to {curGsiStr} — team spans may differ"
            });
        }

        // Deduplicate time disconnects (one per DOW is enough, not per profile)
        disconnects = disconnects
            .GroupBy(d => d.Description)
            .Select(g => g.First())
            .ToList();

        return disconnects;
    }

    // ══════════════════════════════════════════════════════════
    // Private: Field Name Mapping (Source → Current)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Build a mapping from source field names to current field names.
    /// Stage 1: exact name match (case-insensitive). Stage 2: address-based fallback
    /// for fields that were renamed but share the same physical address.
    /// Returns only entries where source name differs from current name.
    /// </summary>
    private async Task<Dictionary<string, string>> BuildFieldNameMapAsync(
        List<GamePlacementPattern> patterns,
        Guid leagueId, string season,
        CancellationToken ct)
    {
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var currentFieldNames = currentFields
            .Select(f => f.FName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Distinct source field names from patterns
        var sourceFieldNames = patterns
            .Select(p => p.FieldName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Stage 1: names that already exist in current — no mapping needed
        var unmatched = new List<string>();
        foreach (var name in sourceFieldNames)
        {
            if (currentFieldNames.Contains(name))
                continue; // identity — no entry needed in map
            unmatched.Add(name);
        }

        if (unmatched.Count == 0)
            return map;

        // Stage 2: address-based fallback
        var sourceFieldIds = patterns
            .Where(p => unmatched.Contains(p.FieldName, StringComparer.OrdinalIgnoreCase))
            .Select(p => p.FieldId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var currentFieldIds = currentFields.Select(f => f.FieldId).ToList();

        var sourceAddresses = sourceFieldIds.Count > 0
            ? await _autoBuildRepo.GetFieldAddressesAsync(sourceFieldIds, ct)
            : new Dictionary<Guid, string>();
        var currentAddresses = currentFieldIds.Count > 0
            ? await _autoBuildRepo.GetFieldAddressesAsync(currentFieldIds, ct)
            : new Dictionary<Guid, string>();

        // Group current fields by normalized address (sorted by name for ordinal matching)
        var currentByAddress = new Dictionary<string, List<string>>();
        foreach (var cf in currentFields.OrderBy(f => f.FName, StringComparer.OrdinalIgnoreCase))
        {
            if (currentAddresses.TryGetValue(cf.FieldId, out var addr))
            {
                if (!currentByAddress.TryGetValue(addr, out var list))
                {
                    list = [];
                    currentByAddress[addr] = list;
                }
                list.Add(cf.FName);
            }
        }

        // Group unmatched source fields by address (sorted by name for ordinal matching)
        var sourceByAddress = new Dictionary<string, List<string>>();
        foreach (var name in unmatched.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var fieldId = patterns
                .FirstOrDefault(p => string.Equals(p.FieldName, name, StringComparison.OrdinalIgnoreCase))
                ?.FieldId;

            if (fieldId.HasValue && fieldId.Value != Guid.Empty
                && sourceAddresses.TryGetValue(fieldId.Value, out var addr))
            {
                if (!sourceByAddress.TryGetValue(addr, out var list))
                {
                    list = [];
                    sourceByAddress[addr] = list;
                }
                list.Add(name);
            }
        }

        // Match by ordinal position within each address group:
        // NewEgyptHS-01 → LBTS-01, NewEgyptHS-02 → LBTS-02, etc.
        foreach (var (addr, sourceNames) in sourceByAddress)
        {
            if (!currentByAddress.TryGetValue(addr, out var currentNames))
                continue;

            for (var i = 0; i < sourceNames.Count && i < currentNames.Count; i++)
                map[sourceNames[i]] = currentNames[i];
        }

        return map;
    }

    /// <summary>
    /// Translate a profile's FieldBand and FieldDesirability keys from source names to current names.
    /// </summary>
    private static DivisionSizeProfile ApplyFieldNameMap(
        DivisionSizeProfile profile,
        Dictionary<string, string> fieldMap)
    {
        var translatedBand = profile.FieldBand
            .Select(name => fieldMap.TryGetValue(name, out var mapped) ? mapped : name)
            .ToList();

        var translatedDesirability = profile.FieldDesirability
            .GroupBy(kvp => fieldMap.TryGetValue(kvp.Key, out var mapped) ? mapped : kvp.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        return profile with
        {
            FieldBand = translatedBand,
            FieldDesirability = translatedDesirability
        };
    }

    // ══════════════════════════════════════════════════════════
    // Save Strategy Profiles (standalone — no build required)
    // ══════════════════════════════════════════════════════════

    public async Task<DivisionStrategyProfileResponse> SaveStrategyProfilesAsync(
        Guid jobId, List<DivisionStrategyEntry> strategies, CancellationToken ct = default)
    {
        var profilesToSave = strategies
            .Select(s => new Domain.Entities.DivisionScheduleProfile
            {
                ProfileId = Guid.NewGuid(),
                JobId = jobId,
                DivisionName = s.DivisionName,
                Placement = (byte)s.Placement,
                GapPattern = (byte)s.GapPattern
            })
            .ToList();

        await _divisionProfileRepo.UpsertBatchAsync(jobId, profilesToSave, ct);
        await _divisionProfileRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveStrategyProfiles: Saved {Count} profiles for Job={JobId}",
            profilesToSave.Count, jobId);

        // Reload and return the updated response
        return await LoadStrategyProfilesAsync(jobId, null, ct);
    }

    // Ensure Pairings (auto-generate missing round-robin)
    // ══════════════════════════════════════════════════════════

    public async Task<EnsurePairingsResponse> EnsurePairingsAsync(
        Guid jobId, string userId, EnsurePairingsRequest request, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        // Find which team counts already have pairings
        var existing = await _pairingsRepo.GetDistinctPoolSizesWithPairingsAsync(leagueId, season, ct);

        var generated = new List<int>();
        var alreadyExisted = new List<int>();

        // Process sequentially (shared DbContext)
        foreach (var tCnt in request.TeamCounts)
        {
            if (existing.Contains(tCnt))
            {
                if (request.ForceRegenerate)
                {
                    // Delete existing pairings so we can regenerate with new round count
                    await _pairingsRepo.DeleteAllAsync(leagueId, season, tCnt, ct);
                    await _pairingsRepo.SaveChangesAsync(ct);
                }
                else
                {
                    alreadyExisted.Add(tCnt);
                    continue;
                }
            }

            // Round count: respect explicit override, then game guarantee, then full RR
            var fullRr = tCnt % 2 == 0 ? tCnt - 1 : tCnt;
            int noRounds;
            if (request.RoundsOverrides?.TryGetValue(tCnt, out var ovr) == true)
                noRounds = Math.Clamp(ovr, 1, fullRr);
            else
                noRounds = ComputeRoundCount(tCnt, request.GameGuarantee);
            await _pairingsService.AddPairingBlockAsync(
                jobId, userId,
                new AddPairingBlockRequest { TeamCount = tCnt, NoRounds = noRounds },
                ct);
            generated.Add(tCnt);
        }

        return new EnsurePairingsResponse
        {
            Generated = generated,
            AlreadyExisted = alreadyExisted
        };
    }

    // ══════════════════════════════════════════════════════════
    // Source Preconfiguration (returning tournament carry-forward)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Compute year delta between source and current job years.
    /// Returns 0 if either year is non-numeric.
    /// </summary>
    private static int ComputeYearDelta(string? sourceYear, string? currentYear)
    {
        if (int.TryParse(currentYear, out var curr) && int.TryParse(sourceYear, out var src))
            return curr - src;
        return 0;
    }

    /// <summary>
    /// Remap source division agegroup names by offsetting graduation years.
    /// "2026 Boys" + delta=1 → "2027 Boys" so tcntLookup matches current agegroups.
    /// </summary>
    private static List<SourceDivisionSummary> RemapSourceDivisionNames(
        List<SourceDivisionSummary> sourceDivisions, int yearDelta)
    {
        var nameMap = AgegroupNameMapper.BuildNameMap(
            sourceDivisions.Select(d => d.AgegroupName).Distinct(), yearDelta);

        if (nameMap.Count == 0) return sourceDivisions;

        return sourceDivisions.Select(d =>
        {
            var mappedName = nameMap.GetValueOrDefault(d.AgegroupName, d.AgegroupName);
            return d with { AgegroupName = mappedName };
        }).ToList();
    }

    /// <summary>
    /// Carry forward agegroup colors from the source job to the current job.
    /// Maps graduation-year agegroup names (e.g., "2026 Boys" → "2027 Boys").
    /// Only sets color on current agegroups that don't already have one.
    /// </summary>
    public async Task<int> ApplyColorSeedAsync(
        Guid jobId, Guid sourceJobId, CancellationToken ct = default)
    {
        var (leagueId, _, year) = await _contextResolver.ResolveAsync(jobId, ct);
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct);
        var yearDelta = ComputeYearDelta(sourceYear, year);

        var sourceMeta = await _autoBuildRepo.GetSourceAgegroupMetaAsync(sourceJobId, ct);
        var currentAgegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, ct);

        var nameMap = yearDelta != 0
            ? AgegroupNameMapper.BuildNameMap(sourceMeta.Select(m => m.AgegroupName), yearDelta)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var applied = 0;
        foreach (var src in sourceMeta.Where(m => !string.IsNullOrEmpty(m.Color)))
        {
            var mappedName = nameMap.GetValueOrDefault(src.AgegroupName, src.AgegroupName);
            var current = currentAgegroups.FirstOrDefault(
                a => string.Equals(a.AgegroupName, mappedName, StringComparison.OrdinalIgnoreCase));

            if (current != null && string.IsNullOrEmpty(current.Color))
            {
                current.Color = src.Color;
                applied++;
            }
        }

        if (applied > 0)
            await _agegroupRepo.SaveChangesAsync(ct);

        return applied;
    }

    /// <summary>
    /// Seed game dates from the source job, advancing by yearDelta and matching DOW.
    /// Only seeds agegroups that don't already have dates configured.
    /// </summary>
    public async Task<DateSeedResult> SeedDatesFromSourceAsync(
        Guid jobId, string userId, Guid sourceJobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct);
        var yearDelta = ComputeYearDelta(sourceYear, year);
        if (yearDelta == 0)
            return new DateSeedResult { AgegroupsSeeded = 0 };

        var sourceDates = await _autoBuildRepo.GetSourceDatesAsync(sourceJobId, ct);
        var nameMap = AgegroupNameMapper.BuildNameMap(sourceDates.Keys, yearDelta);

        var currentAgegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, ct);
        var existingDates = await _timeslotRepo.GetAgegroupIdsWithDatesAsync(
            leagueId, season, year, ct);

        var seeded = 0;
        foreach (var (srcAgName, entries) in sourceDates)
        {
            var mappedName = nameMap.GetValueOrDefault(srcAgName, srcAgName);
            var currentAg = currentAgegroups.FirstOrDefault(
                a => string.Equals(a.AgegroupName, mappedName, StringComparison.OrdinalIgnoreCase));

            if (currentAg == null) continue;
            if (existingDates.Contains(currentAg.AgegroupId)) continue;

            foreach (var entry in entries)
            {
                var newDate = AdvanceDateToMatchDow(entry.GDate, yearDelta);
                _timeslotRepo.AddDate(new TimeslotsLeagueSeasonDates
                {
                    AgegroupId = currentAg.AgegroupId,
                    GDate = newDate,
                    Rnd = entry.Rnd,
                    Season = season,
                    Year = year,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                });
            }
            seeded++;
        }

        if (seeded > 0)
            await _timeslotRepo.SaveChangesAsync(ct);

        return new DateSeedResult { AgegroupsSeeded = seeded };
    }

    /// <summary>
    /// Advance a date by N years, then adjust ±1-3 days to land on the same DOW.
    /// Example: Saturday March 5, 2026 + 1 year → lands on Friday March 5, 2027 → adjust to Saturday March 6, 2027.
    /// </summary>
    private static DateTime AdvanceDateToMatchDow(DateTime sourceDate, int yearDelta)
    {
        var target = sourceDate.AddYears(yearDelta);
        var dowDiff = (int)sourceDate.DayOfWeek - (int)target.DayOfWeek;
        if (dowDiff > 3) dowDiff -= 7;
        if (dowDiff < -3) dowDiff += 7;
        return target.AddDays(dowDiff);
    }

    /// <summary>
    /// Learn field assignments from the source schedule and seed field-timeslot rows.
    /// Observes which fields each agegroup actually used in last year's games,
    /// maps field names (address-based), and creates TimeslotsLeagueSeasonFields rows
    /// with GSI/timing defaults from the extracted profile.
    /// Only seeds agegroups that don't already have field-timeslot config.
    /// </summary>
    public async Task<FieldSeedResult> SeedFieldAssignmentsFromSourceAsync(
        Guid jobId, string userId, Guid sourceJobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct);
        var yearDelta = ComputeYearDelta(sourceYear, year);

        // 1. Get source field usage patterns (agegroupName → fields used)
        var sourceUsage = await _autoBuildRepo.GetSourceFieldUsageAsync(sourceJobId, ct);

        // 2. Build name maps (agegroup year offset + field name mapping)
        var nameMap = yearDelta != 0
            ? AgegroupNameMapper.BuildNameMap(sourceUsage.Keys, yearDelta)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var patterns = await _autoBuildRepo.ExtractPatternAsync(sourceJobId, ct);
        var fieldMap = await BuildFieldNameMapAsync(patterns, leagueId, season, ct);

        // 3. Get current state
        var currentAgegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, ct);
        var existingFields = await _timeslotRepo.GetAgegroupIdsWithFieldTimeslotsAsync(
            leagueId, season, year, ct);
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var currentFieldByName = currentFields.ToDictionary(
            f => f.FName, f => f.FieldId, StringComparer.OrdinalIgnoreCase);

        // 4. Extract source profiles for GSI/timing defaults
        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(sourceJobId, ct);
        if (yearDelta != 0)
            sourceDivisions = RemapSourceDivisionNames(sourceDivisions, yearDelta);
        var sourceWindow = await GetSourceTimeslotWindowAsync(sourceJobId, ct);
        var profiles = AttributeExtractor.ExtractProfiles(
            patterns, sourceDivisions, null, sourceWindow);

        var seeded = 0;
        var newTimeslots = new List<TimeslotsLeagueSeasonFields>();

        foreach (var (srcAgName, fieldUsages) in sourceUsage)
        {
            var mappedName = nameMap.GetValueOrDefault(srcAgName, srcAgName);
            var currentAg = currentAgegroups.FirstOrDefault(
                a => string.Equals(a.AgegroupName, mappedName, StringComparison.OrdinalIgnoreCase));

            if (currentAg == null) continue;
            if (existingFields.Contains(currentAg.AgegroupId)) continue;

            // Look up profile defaults from the source division with same agegroup name
            var srcDiv = sourceDivisions.FirstOrDefault(
                d => string.Equals(d.AgegroupName, mappedName, StringComparison.OrdinalIgnoreCase));
            var profile = srcDiv != null ? profiles.GetValueOrDefault(srcDiv.TeamCount) : null;
            var defaultGsi = profile?.GsiMinutes ?? 45;

            foreach (var usage in fieldUsages)
            {
                // Map source field name → current field ID
                var resolvedName = fieldMap.GetValueOrDefault(usage.FieldName, usage.FieldName);
                if (!currentFieldByName.TryGetValue(resolvedName, out var fieldId)) continue;

                foreach (var dow in usage.DaysUsed)
                {
                    var dowStr = dow.ToString(); // Full name: "Saturday", "Sunday", etc.
                    var startTimeRange = profile?.TimeRangeAbsolute.GetValueOrDefault(dow);
                    var startTimeStr = startTimeRange != null
                        ? DateTime.Today.Add(startTimeRange.Start).ToString("hh:mm tt")
                        : "08:00 AM";
                    // MaxGamesPerField = capacity ceiling (# rows the scheduler can use).
                    // Derive from source time window; generous fallback for new jobs.
                    // NEVER extend past end of day — cap at (midnight - startTime) / GSI.
                    int maxGames;
                    if (startTimeRange != null && defaultGsi > 0)
                    {
                        var windowMinutes = (startTimeRange.End - startTimeRange.Start).TotalMinutes;
                        maxGames = Math.Max(2, (int)Math.Floor(windowMinutes / defaultGsi));
                    }
                    else
                    {
                        // No time window data — compute from start time to 10 PM hard cap
                        var startMinutes = startTimeRange?.Start.TotalMinutes ?? 480; // 8 AM default
                        var availableMinutes = Math.Min(660, 1320 - startMinutes); // 10 PM = 1320 min
                        maxGames = defaultGsi > 0 ? Math.Max(2, (int)(availableMinutes / defaultGsi)) : 14;
                    }

                    newTimeslots.Add(new TimeslotsLeagueSeasonFields
                    {
                        AgegroupId = currentAg.AgegroupId,
                        FieldId = fieldId,
                        Season = season,
                        Year = year,
                        Dow = dowStr,
                        GamestartInterval = defaultGsi,
                        MaxGamesPerField = maxGames,
                        StartTime = startTimeStr,
                        LebUserId = userId,
                        Modified = DateTime.UtcNow
                    });
                }
            }
            seeded++;
        }

        if (newTimeslots.Count > 0)
            await _timeslotRepo.AddFieldTimeslotsRangeAsync(newTimeslots, ct);

        return new FieldSeedResult { AgegroupsSeeded = seeded, TimeslotRowsCreated = newTimeslots.Count };
    }

    public async Task<PreconfigureResult> PreconfigureFromSourceAsync(
        Guid jobId, string userId, Guid sourceJobId, CancellationToken ct = default)
    {
        // 1. Colors
        var colorsApplied = await ApplyColorSeedAsync(jobId, sourceJobId, ct);

        // 2. Dates
        var dateResult = await SeedDatesFromSourceAsync(jobId, userId, sourceJobId, ct);

        // 3. Field assignments
        var fieldResult = await SeedFieldAssignmentsFromSourceAsync(jobId, userId, sourceJobId, ct);

        // 4. Pairings — auto-detect team counts from current divisions
        var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var teamCounts = currentDivisions
            .Where(d => d.TeamCount >= 2)
            .Select(d => d.TeamCount)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var pairingsResult = new EnsurePairingsResponse
        {
            Generated = new List<int>(),
            AlreadyExisted = new List<int>()
        };

        if (teamCounts.Count > 0)
        {
            pairingsResult = await EnsurePairingsAsync(jobId, userId, new EnsurePairingsRequest
            {
                TeamCounts = teamCounts
            }, ct);
        }

        return new PreconfigureResult
        {
            ColorsApplied = colorsApplied,
            DatesSeeded = dateResult.AgegroupsSeeded,
            FieldAssignmentsSeeded = fieldResult.AgegroupsSeeded,
            FieldTimeslotRowsCreated = fieldResult.TimeslotRowsCreated,
            PairingsGenerated = pairingsResult.Generated,
            PairingsAlreadyExisted = pairingsResult.AlreadyExisted
        };
    }

}
