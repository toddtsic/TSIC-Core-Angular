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

        // Filter out inactive agegroups (WAITLIST, DROPPED) and placeholder divisions (Unassigned)
        var activeDivisions = divisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase));

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

        return new GameSummaryResponse
        {
            JobName = jobName,
            TotalGames = totalGames,
            TotalDivisions = summaries.Count,
            DivisionsWithGames = divsWithGames,
            Divisions = summaries
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
        //    Exclude WAITLIST/DROPPED agegroups and Unassigned divisions
        var divisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var schedulableDivisions = divisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase));
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
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        // Extract raw patterns and division summaries from source
        var patterns = await _autoBuildRepo.ExtractPatternAsync(sourceJobId, ct);
        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(sourceJobId, ct);

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
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct) ?? "";

        return new ProfileExtractionResponse
        {
            SourceJobId = sourceJobId,
            SourceJobName = sourceName,
            SourceYear = sourceYear,
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
        var activeDivisions = allDivisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .ToList();

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

            // Delete existing games before rebuilding
            if (existingCounts.GetValueOrDefault(div.DivId) > 0)
            {
                await _scheduleRepo.DeleteDivisionGamesAsync(
                    div.DivId, leagueId, season, year, ct);
                await _scheduleRepo.SaveChangesAsync(ct);
            }

            // Get agegroup/division metadata
            var agegroup = await _agegroupRepo.GetByIdAsync(div.AgegroupId, ct);
            var division = await _divisionRepo.GetByIdReadOnlyAsync(div.DivId, ct);
            var agSeason = agegroup?.Season ?? season;
            var agLeagueId = agegroup?.LeagueId ?? leagueId;

            // Get pairings
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
                agLeagueId, agSeason, teamCount, ct);
            var rrPairings = pairings
                .Where(p => p.T1Type == "T" && p.T2Type == "T")
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

            // Generate candidate slots for this agegroup/division
            var dates = await _timeslotRepo.GetDatesAsync(div.AgegroupId, season, year, ct);
            var fields = await _timeslotRepo.GetFieldTimeslotsAsync(div.AgegroupId, season, year, ct);

            // Use division-specific timeslots if available, otherwise agegroup-level
            var divDates = dates.Where(d => d.DivId == div.DivId).ToList();
            var effectiveDates = divDates.Count > 0
                ? divDates : dates.Where(d => d.DivId == null).ToList();

            var divFields = fields.Where(f => f.DivId == div.DivId).ToList();
            var effectiveFields = divFields.Count > 0
                ? divFields : fields.Where(f => f.DivId == null).ToList();

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

            // Build candidate slots: date × field × game intervals
            var candidates = GenerateCandidateSlots(effectiveDates, effectiveFields);

            // Current year's GSI from field config
            var currentGsi = effectiveFields.Count > 0
                ? effectiveFields.Max(f => f.GamestartInterval) : 60;

            // Get or build profile for this division
            DivisionSizeProfile profile;
            if (useStrategyPath && strategyByName.TryGetValue(div.DivName, out var strategy))
            {
                // Strategy-driven: translate user choices to profile
                profile = BuildProfileFromStrategy(
                    strategy, teamCount, effectiveDates, effectiveFields, currentGsi);
            }
            else
            {
                // Legacy TCnt-keyed path or clean sheet
                profile = GetOrBuildDefaultProfile(
                    profilesByTCnt, teamCount, effectiveDates, effectiveFields, currentGsi);
            }

            // Play days are actual DayOfWeek — no ordinal remapping.
            // If source days don't match current timeslot days, the scorer penalizes
            // wrong-day slots rather than silently remapping Saturday→Sunday.

            // Compute current job's timeslot window start per DOW from field config
            var currentWindowStart = new Dictionary<DayOfWeek, TimeSpan>();
            foreach (var field in effectiveFields)
            {
                if (!TimeSpan.TryParse(field.StartTime, out var st)) continue;
                if (!Enum.TryParse<DayOfWeek>(field.Dow, true, out var dow)) continue;
                if (!currentWindowStart.TryGetValue(dow, out var existing) || st < existing)
                    currentWindowStart[dow] = st;
            }

            // Build PlacementState for this division (shares global occupiedSlots + field prefs)
            var state = new PlacementState(occupiedSlots, currentWindowStart, fieldPreferences);

            // Group pairings by round
            var roundsByNum = rrPairings
                .GroupBy(p => p.Rnd)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.GameNumber).ToList());

            // Compute effective play days: profile's PlayDays filtered to days that
            // actually exist in this division's candidates. Profiles are keyed by TCnt,
            // so a profile may contain PlayDays from multiple agegroups (e.g., Sat from
            // 2030 + Sun from 2034) even though this division only plays one day.
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

            // Resolve wave from strategy entry (defaults to 1 if not set)
            var divWave = useStrategyPath && strategyByName.TryGetValue(div.DivName, out var divStrategy)
                ? Math.Max(1, divStrategy.Wave)
                : 1;

            divContexts.Add(new DivisionBuildContext
            {
                Div = div,
                Agegroup = agegroup,
                Division = division,
                AgSeason = agSeason,
                AgLeagueId = agLeagueId,
                TeamCount = teamCount,
                Candidates = candidates,
                Profile = profile,
                State = state,
                CurrentWindowStart = currentWindowStart,
                RoundsByNum = roundsByNum,
                MaxRound = roundsByNum.Keys.DefaultIfEmpty(0).Max(),
                EffectivePlayDays = effectivePlayDays,
                Wave = divWave
            });
        }

        // ── 6b. Refresh occupiedSlots after deletions ──
        // The initial load (step 3) captured slots for games that were just
        // deleted in the loop above. Those "ghost" entries would incorrectly
        // block placement. Mutate the existing HashSet instance so all
        // PlacementState references see the updated set.
        if (existingCounts.Values.Any(c => c > 0))
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

        // ── 7. Division-sequential placement ──
        // Place ALL rounds for each division before moving to the next.
        // This keeps each team's games clustered in time (tight span),
        // matching the source's ~180-min team spans.
        //
        // Previous interleaved strategy (R1-all, R2-all, R3-all) consumed
        // nearby slots across divisions, forcing later rounds into distant
        // time slots — inflating spans to 315-450+ minutes despite the
        // span penalty. Division-sequential lets each division claim a
        // contiguous time block, then the next division claims the next block.
        //
        // Division processing order is set by BuildProcessingOrder
        // (agegroup priority + division strategy).

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
        // Strategy-driven path doesn't use source offsets, so this is not populated.
        var tcntDayFirstPlaced = new HashSet<(int TCnt, DayOfWeek Day)>();

        // ── Wave-grouped placement ──
        // Group divisions by wave number, process each wave sequentially.
        // After each wave completes, set a time floor so subsequent waves
        // can't place games before the latest game in the completed wave.
        // When all divisions are Wave 1 (default), this collapses to the
        // original flat loop with no time floor — zero behavioral change.
        var waveGroups = divContexts
            .GroupBy(c => c.Wave)
            .OrderBy(g => g.Key)
            .ToList();

        DateTime? waveTimeFloor = null;
        DateTime? waveLatestPlacement = null;

        foreach (var waveGroup in waveGroups)
        {
            waveLatestPlacement = null;

            foreach (var ctx in waveGroup)
            {
            for (var roundNum = 1; roundNum <= ctx.MaxRound; roundNum++)
            {
                if (!ctx.RoundsByNum.TryGetValue(roundNum, out var gamesInRound))
                    continue;

                // Compute target day for this round.
                // Uses EffectivePlayDays (profile days filtered to candidate days) so
                // single-day agegroups don't get false wrong-day penalties.
                DayOfWeek? targetDay = null;
                if (ctx.EffectivePlayDays.Count > 0)
                {
                    var dayIndex = (roundNum - 1) % ctx.EffectivePlayDays.Count;
                    targetDay = ctx.EffectivePlayDays[dayIndex];
                }

                // Compute round start time from source attributes:
                // 1. If round already has a target (first game placed) → use it
                // 2. Find previous round on same day → advance by inter-round interval
                // 3. First round on this day for the FIRST division of this TCnt →
                //    use start tick offset from window (source pattern)
                // 4. First round on this day for SUBSEQUENT divisions of same TCnt →
                //    no target time (let scorer find best available slot)
                //
                // Why suppress StartTickOffset for later divisions:
                // With division-sequential placement, division #1 claims slots near
                // the source offset. Division #2 targeting the same offset would get
                // target-time penalties pushing it toward occupied slots, degrading
                // placement quality. Better to let it float to whatever's available.
                var roundKey = (ctx.Div.DivId, roundNum);
                TimeSpan? roundStartTime = null;

                if (ctx.State.RoundTargetTimes.TryGetValue(roundKey, out var existingTarget))
                {
                    roundStartTime = existingTarget;
                }
                else
                {
                    // Look for previous round on same target day
                    TimeSpan? prevRoundTime = null;
                    var prevRoundNum = 0;
                    if (targetDay.HasValue && ctx.EffectivePlayDays.Count > 0)
                    {
                        for (var prev = roundNum - 1; prev >= 1; prev--)
                        {
                            var prevDayIndex = (prev - 1) % ctx.EffectivePlayDays.Count;
                            if (ctx.EffectivePlayDays[prevDayIndex] == targetDay.Value
                                && ctx.State.RoundTargetTimes.TryGetValue((ctx.Div.DivId, prev), out var pt))
                            {
                                prevRoundTime = pt;
                                prevRoundNum = prev;
                                break;
                            }
                        }
                    }

                    if (prevRoundTime.HasValue && ctx.Profile.GsiMinutes > 0)
                    {
                        // Subsequent round on same day: advance by enough ticks to avoid BTBs.
                        //
                        // For sequential layout, the previous round's games span ticks
                        // [prevStart .. prevStart + gamesInPrevRound - 1]. The worst-case
                        // BTB is: team plays the LAST game of prev round and the FIRST game
                        // of this round. To guarantee MinTeamGapTicks gap:
                        //   thisRoundStart >= prevStart + gamesInPrevRound - 1 + MinTeamGapTicks
                        //   i.e., gap in ticks >= gamesInPrevRound + MinTeamGapTicks - 1
                        //
                        // For horizontal layout, all games are at the same tick, so the
                        // minimum gap is just MinTeamGapTicks.
                        var prevGameCount = ctx.RoundsByNum.TryGetValue(prevRoundNum, out var prevGames)
                            ? prevGames.Count : 0;

                        int minGapTicks;
                        if (ctx.Profile.RoundLayout == RoundLayout.Sequential && prevGameCount > 1)
                            minGapTicks = prevGameCount + ctx.Profile.MinTeamGapTicks - 1;
                        else
                            minGapTicks = ctx.Profile.MinTeamGapTicks;

                        // Use the larger of source InterRoundGapTicks and the BTB-safe minimum
                        var effectiveGapTicks = Math.Max(ctx.Profile.InterRoundGapTicks, minGapTicks);

                        roundStartTime = prevRoundTime.Value
                            + TimeSpan.FromMinutes(effectiveGapTicks * ctx.Profile.GsiMinutes);
                    }
                    else if (targetDay.HasValue
                             && !tcntDayFirstPlaced.Contains((ctx.TeamCount, targetDay.Value)))
                    {
                        // First division of this TCnt on this day — use source offset
                        if (ctx.Profile.StartTickOffset != null
                            && ctx.Profile.StartTickOffset.TryGetValue(targetDay.Value, out var tickOffset)
                            && ctx.CurrentWindowStart.TryGetValue(targetDay.Value, out var winStart)
                            && ctx.Profile.GsiMinutes > 0)
                        {
                            roundStartTime = winStart + TimeSpan.FromMinutes(tickOffset * ctx.Profile.GsiMinutes);
                        }
                        else if (ctx.Profile.StartOffsetFromWindow != null
                                 && ctx.Profile.StartOffsetFromWindow.TryGetValue(targetDay.Value, out var offset)
                                 && ctx.CurrentWindowStart.TryGetValue(targetDay.Value, out var winStart2))
                        {
                            roundStartTime = winStart2 + offset;
                        }
                    }
                    // else: subsequent division of same TCnt on same day — no target time.
                    // Scorer picks best available based on span, gap, day, and field balance.
                }

                // Place each game in this round
                var gameIndex = 0;
                foreach (var pairing in gamesInRound)
                {
                    // Per-game target time: for sequential rounds, each game advances by 1 GSI tick
                    var gameTargetTime = roundStartTime;
                    if (ctx.Profile.RoundLayout == RoundLayout.Sequential
                        && roundStartTime.HasValue && ctx.Profile.GsiMinutes > 0 && gameIndex > 0)
                    {
                        gameTargetTime = roundStartTime.Value
                            + TimeSpan.FromMinutes(gameIndex * ctx.Profile.GsiMinutes);
                    }
                    gameIndex++;

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
                        TargetDay = targetDay,
                        TargetTime = gameTargetTime
                    };

                    var best = PlacementScorer.FindBestSlot(
                        ctx.Candidates, game, ctx.Profile, ctx.State, waveTimeFloor);

                    if (best == null)
                    {
                        divFailedCounts[ctx.Div.DivId]++;
                        unplacedGames.Add(new UnplacedGameDto
                        {
                            AgegroupName = ctx.Div.AgegroupName,
                            DivName = ctx.Div.DivName,
                            Round = roundNum,
                            T1No = pairing.T1,
                            T2No = pairing.T2,
                            Reason = "No available slot (all occupied)"
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
                        LeagueId = ctx.AgLeagueId,
                        Season = ctx.AgSeason,
                        Year = year,
                        AgegroupId = ctx.Div.AgegroupId,
                        AgegroupName = ctx.Agegroup?.AgegroupName ?? "",
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

                    // Track latest placement time for wave floor computation
                    if (!waveLatestPlacement.HasValue || best.Slot.GDate > waveLatestPlacement.Value)
                        waveLatestPlacement = best.Slot.GDate;

                    // Mark this TCnt+Day as having its first division placed.
                    // Subsequent divisions with same TCnt won't use source StartTickOffset.
                    if (targetDay.HasValue)
                        tcntDayFirstPlaced.Add((ctx.TeamCount, targetDay.Value));
                }
            }
        } // end foreach ctx in waveGroup

            // After all divisions in this wave are placed, set the wave time floor.
            // Wave 2+ candidates must start at or after the latest placed game + 1 GSI.
            // This ensures young agegroups finish before older ones start.
            if (waveGroups.Count > 1 && waveLatestPlacement.HasValue)
            {
                var gsiMinutes = waveGroup.FirstOrDefault()?.Profile.GsiMinutes ?? 60;
                waveTimeFloor = waveLatestPlacement.Value.AddMinutes(gsiMinutes);
            }
        } // end foreach waveGroup

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

        // ── 7. Build sacrifice log from penalty breakdown ──
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

        var scheduled = divisionResults.Count(r =>
            r.Status != "excluded" && r.Status != "no-teams" && r.Status != "no-pairings");
        var skipped = divisionResults.Count - scheduled;

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
                    GapPattern = (byte)s.GapPattern,
                    Wave = (byte)Math.Clamp(s.Wave, 1, 9)
                })
                .ToList();

            await _divisionProfileRepo.UpsertBatchAsync(jobId, profilesToSave, ct);
            await _divisionProfileRepo.SaveChangesAsync(ct);

            _logger.LogInformation(
                "AutoBuild: Saved {Count} strategy profiles for Job={JobId}",
                profilesToSave.Count, jobId);
        }

        _logger.LogInformation(
            "AutoBuild: Job={JobId}, Divisions={Total}, Scheduled={Scheduled}, " +
            "GamesPlaced={Placed}, GamesFailed={Failed}, Sacrifices={SacrificeCount}",
            jobId, orderedDivisions.Count, scheduled, totalPlaced, totalFailed, sacrificeLog.Count);

        return new AutoBuildResult
        {
            TotalDivisions = orderedDivisions.Count,
            DivisionsScheduled = scheduled,
            DivisionsSkipped = skipped,
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
                    GapPattern = p.GapPattern,
                    Wave = p.Wave
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

                // Detect wave boundaries from source game patterns.
                // Groups source games by agegroup, computes time envelopes,
                // and assigns wave numbers where natural time gaps exist.
                var waveByAgegroup = DetectWaveAssignments(patterns);
                if (waveByAgegroup.Count > 0)
                {
                    // Map division name → agegroup name(s) from current year's divisions
                    var divToAgegroup = currentDivisions
                        .GroupBy(d => d.DivName, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key,
                            g => g.First().AgegroupName,
                            StringComparer.OrdinalIgnoreCase);

                    strategies = strategies.Select(s =>
                    {
                        if (divToAgegroup.TryGetValue(s.DivisionName, out var agName)
                            && waveByAgegroup.TryGetValue(agName, out var wave))
                        {
                            return s with { Wave = wave };
                        }
                        return s;
                    }).ToList();
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
    /// excludes Waitlist/Dropped agegroups and Unassigned divisions.
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
                     && !d.DivName.Contains("Unassigned", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ══════════════════════════════════════════════════════════
    // Private: Division Build Context
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Pre-computed context for one division, built during the async first pass.
    /// Used by the interleaved placement loop (round-by-round across all divisions).
    /// </summary>
    private sealed class DivisionBuildContext
    {
        public required CurrentDivisionSummary Div { get; init; }
        public Agegroups? Agegroup { get; init; }
        public Divisions? Division { get; init; }
        public required string AgSeason { get; init; }
        public required Guid AgLeagueId { get; init; }
        public required int TeamCount { get; init; }
        public required List<CandidateSlot> Candidates { get; init; }
        public required DivisionSizeProfile Profile { get; init; }
        public required PlacementState State { get; init; }
        public required Dictionary<DayOfWeek, TimeSpan> CurrentWindowStart { get; init; }
        public required Dictionary<int, List<PairingsLeagueSeason>> RoundsByNum { get; init; }
        public required int MaxRound { get; init; }

        /// <summary>
        /// Profile PlayDays filtered to only include days that actually exist in this
        /// division's candidate slots. Prevents false wrong-day penalties when profiles
        /// are shared across agegroups that play on different days (same TCnt, different DOW).
        /// </summary>
        public required List<DayOfWeek> EffectivePlayDays { get; init; }

        /// <summary>
        /// Wave group (1-based). Engine completes all Wave 1 divisions before starting Wave 2.
        /// </summary>
        public int Wave { get; init; } = 1;
    }

    // ══════════════════════════════════════════════════════════
    // Private: Wave Detection
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Detect wave boundaries from source game patterns.
    /// Analyzes per-agegroup time envelopes and finds natural gaps where one group
    /// of agegroups finishes before another starts (e.g., young AM / old PM).
    /// Returns empty dictionary if no wave pattern detected (all overlap or single agegroup).
    /// </summary>
    private static Dictionary<string, int> DetectWaveAssignments(
        List<GamePlacementPattern> patterns)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Only analyze round-robin games (T vs T)
        var rrGames = patterns
            .Where(p => p.T1Type == "T" && p.T2Type == "T")
            .ToList();

        if (rrGames.Count == 0)
            return result;

        // Compute time envelope per agegroup: (earliest game start, latest game start)
        // Use TimeOfDay (abstracted from literal dates) for year-agnostic comparison.
        var agEnvelopes = rrGames
            .GroupBy(g => g.AgegroupName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                AgegroupName = g.Key,
                Earliest = g.Min(p => p.TimeOfDay),
                Latest = g.Max(p => p.TimeOfDay)
            })
            .OrderBy(e => e.Earliest)
            .ThenBy(e => e.Latest)
            .ToList();

        if (agEnvelopes.Count <= 1)
            return result; // Single agegroup — no waves possible

        // Detect natural gaps: walk sorted agegroups, mark wave boundaries
        // where agegroup N's latest game < agegroup N+1's earliest game.
        // A gap of even 1 minute counts — if they don't overlap, they're separate waves.
        var waveNumber = 1;
        result[agEnvelopes[0].AgegroupName] = waveNumber;

        for (var i = 1; i < agEnvelopes.Count; i++)
        {
            var prev = agEnvelopes[i - 1];
            var curr = agEnvelopes[i];

            // If current agegroup's earliest game starts after previous agegroup's
            // latest game, this is a wave boundary.
            if (curr.Earliest > prev.Latest)
                waveNumber++;

            result[curr.AgegroupName] = waveNumber;
        }

        // Only return waves if we detected more than one
        if (waveNumber <= 1)
            return new Dictionary<string, int>();

        return result;
    }

    // ══════════════════════════════════════════════════════════
    // Private: Processing Order
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Build the ordered list of divisions to process based on agegroup order and division strategy.
    /// </summary>
    private static List<CurrentDivisionSummary> BuildProcessingOrder(
        List<CurrentDivisionSummary> activeDivisions,
        List<Guid> agegroupOrder,
        string divisionOrderStrategy,
        HashSet<Guid> excludedIds)
    {
        // Group divisions by agegroup
        var divsByAgegroup = activeDivisions
            .GroupBy(d => d.AgegroupId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<CurrentDivisionSummary>();

        // Process agegroups in specified order
        foreach (var agId in agegroupOrder)
        {
            if (!divsByAgegroup.TryGetValue(agId, out var divisions))
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
        var processedAgIds = new HashSet<Guid>(agegroupOrder);
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
                // Parse start time
                if (!TimeSpan.TryParse(field.StartTime, out var startTime))
                    continue;

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
        int currentGsi)
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
                .Select(f => TimeSpan.TryParse(f.StartTime, out var t) ? t : TimeSpan.Zero)
                .Where(t => t > TimeSpan.Zero)
                .ToList();
            if (startTimes.Count == 0) continue;

            var minStart = startTimes.Min();
            var maxInterval = dayFields.Max(f => f.GamestartInterval);
            var maxGames = dayFields.Max(f => f.MaxGamesPerField);
            var maxEnd = minStart + TimeSpan.FromMinutes(maxInterval * maxGames);

            timeRangeAbsolute[day] = new TimeRangeDto { Start = minStart, End = maxEnd };
        }

        // Default round count: TCnt - 1 for even, TCnt for odd (round-robin)
        var roundCount = teamCount % 2 == 0 ? teamCount - 1 : teamCount;
        var gameGuarantee = teamCount - 1;

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
        int currentGsi)
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
                .Select(f => TimeSpan.TryParse(f.StartTime, out var t) ? t : TimeSpan.Zero)
                .Where(t => t > TimeSpan.Zero)
                .ToList();
            if (startTimes.Count == 0) continue;

            var minStart = startTimes.Min();
            var maxInterval = dayFields.Max(f => f.GamestartInterval);
            var maxGames = dayFields.Max(f => f.MaxGamesPerField);
            var maxEnd = minStart + TimeSpan.FromMinutes(maxInterval * maxGames);

            timeRangeAbsolute[day] = new TimeRangeDto { Start = minStart, End = maxEnd };
        }

        var roundCount = teamCount % 2 == 0 ? teamCount - 1 : teamCount;
        var gameGuarantee = teamCount - 1;
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
                if (!TimeSpan.TryParse(field.StartTime, out var startTime))
                    continue;

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

                if (!TimeSpan.TryParse(field.StartTime, out var st)) continue;
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

    /// <summary>
    /// Remap a profile's PlayDays (and all DOW-keyed dictionaries) from source days to current days
    /// by ordinal position. Source day 1 → current day 1, source day 2 → current day 2, etc.
    /// When multiple source days collapse to the same current day, values are merged.
    /// </summary>
    private static DivisionSizeProfile ApplyDayOrdinalMap(
        DivisionSizeProfile profile,
        List<DayOfWeek> currentPlayDays)
    {
        if (profile.PlayDays.Count == 0 || currentPlayDays.Count == 0)
            return profile;

        // Build ordinal map: source day[i] → current day[i]
        var dayMap = new Dictionary<DayOfWeek, DayOfWeek>();
        var limit = Math.Min(profile.PlayDays.Count, currentPlayDays.Count);
        var needsMapping = false;

        for (var i = 0; i < limit; i++)
        {
            dayMap[profile.PlayDays[i]] = currentPlayDays[i];
            if (profile.PlayDays[i] != currentPlayDays[i])
                needsMapping = true;
        }

        // If current has fewer days than source, map remaining source days to the last current day
        for (var i = limit; i < profile.PlayDays.Count; i++)
        {
            dayMap[profile.PlayDays[i]] = currentPlayDays[^1];
            needsMapping = true;
        }

        if (!needsMapping)
            return profile; // Days already match — no translation needed

        DayOfWeek MapDay(DayOfWeek d) => dayMap.TryGetValue(d, out var mapped) ? mapped : d;

        // PlayDays: deduplicate after mapping (preserves order)
        var mappedPlayDays = profile.PlayDays
            .Select(MapDay)
            .Distinct()
            .ToList();

        // TimeRangeAbsolute: merge by widening the window (min start, max end)
        var mappedTimeRange = new Dictionary<DayOfWeek, TimeRangeDto>();
        foreach (var kvp in profile.TimeRangeAbsolute)
        {
            var day = MapDay(kvp.Key);
            if (mappedTimeRange.TryGetValue(day, out var existing))
            {
                mappedTimeRange[day] = new TimeRangeDto
                {
                    Start = existing.Start < kvp.Value.Start ? existing.Start : kvp.Value.Start,
                    End = existing.End > kvp.Value.End ? existing.End : kvp.Value.End
                };
            }
            else
            {
                mappedTimeRange[day] = kvp.Value;
            }
        }

        // StartOffsetFromWindow: take the earliest offset when merging
        Dictionary<DayOfWeek, TimeSpan>? mappedOffset = null;
        if (profile.StartOffsetFromWindow != null)
        {
            mappedOffset = new Dictionary<DayOfWeek, TimeSpan>();
            foreach (var kvp in profile.StartOffsetFromWindow)
            {
                var day = MapDay(kvp.Key);
                if (!mappedOffset.TryGetValue(day, out var existing) || kvp.Value < existing)
                    mappedOffset[day] = kvp.Value;
            }
        }

        // WindowUtilization: take the max when merging
        Dictionary<DayOfWeek, double>? mappedUtil = null;
        if (profile.WindowUtilization != null)
        {
            mappedUtil = new Dictionary<DayOfWeek, double>();
            foreach (var kvp in profile.WindowUtilization)
            {
                var day = MapDay(kvp.Key);
                if (!mappedUtil.TryGetValue(day, out var existing) || kvp.Value > existing)
                    mappedUtil[day] = kvp.Value;
            }
        }

        // OnsiteIntervalPerDay: take the max span when merging
        var mappedOnsite = new Dictionary<DayOfWeek, TimeSpan>();
        foreach (var kvp in profile.OnsiteIntervalPerDay)
        {
            var day = MapDay(kvp.Key);
            if (!mappedOnsite.TryGetValue(day, out var existing) || kvp.Value > existing)
                mappedOnsite[day] = kvp.Value;
        }

        // RoundsPerDay: sum when merging (all rounds now on one day)
        var mappedRounds = new Dictionary<DayOfWeek, int>();
        foreach (var kvp in profile.RoundsPerDay)
        {
            var day = MapDay(kvp.Key);
            mappedRounds[day] = mappedRounds.GetValueOrDefault(day) + kvp.Value;
        }

        return profile with
        {
            PlayDays = mappedPlayDays,
            TimeRangeAbsolute = mappedTimeRange,
            StartOffsetFromWindow = mappedOffset,
            WindowUtilization = mappedUtil,
            OnsiteIntervalPerDay = mappedOnsite,
            RoundsPerDay = mappedRounds,
            ExtraRoundDay = profile.ExtraRoundDay.HasValue ? MapDay(profile.ExtraRoundDay.Value) : null
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
                GapPattern = (byte)s.GapPattern,
                Wave = (byte)Math.Clamp(s.Wave, 1, 9)
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
                alreadyExisted.Add(tCnt);
                continue;
            }

            // Generate standard round-robin: N-1 rounds for N teams
            var noRounds = tCnt - 1;
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

}
