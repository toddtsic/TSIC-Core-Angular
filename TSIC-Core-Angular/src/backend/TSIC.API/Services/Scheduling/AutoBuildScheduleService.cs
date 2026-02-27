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
    private readonly ISchedulingContextResolver _contextResolver;
    private readonly IScheduleDivisionService _scheduleDivisionService;
    private readonly IScheduleQaService _qaService;
    private readonly ILogger<AutoBuildScheduleService> _logger;

    public AutoBuildScheduleService(
        IAutoBuildRepository autoBuildRepo,
        IScheduleRepository scheduleRepo,
        ITimeslotRepository timeslotRepo,
        IPairingsRepository pairingsRepo,
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
        _agegroupRepo = agegroupRepo;
        _divisionRepo = divisionRepo;
        _contextResolver = contextResolver;
        _scheduleDivisionService = scheduleDivisionService;
        _qaService = qaService;
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
    // Phase 1: Source Job Discovery
    // ══════════════════════════════════════════════════════════

    public async Task<List<AutoBuildSourceJobDto>> GetSourceJobsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _autoBuildRepo.GetSourceJobCandidatesAsync(jobId, ct);
    }

    // ══════════════════════════════════════════════════════════
    // Phase 1.5: Agegroup Mapping Proposal
    // ══════════════════════════════════════════════════════════

    public async Task<AgegroupMappingResponse> ProposeAgegroupMappingsAsync(
        Guid jobId, Guid sourceJobId, CancellationToken ct = default)
    {
        var sourceJobName = await _autoBuildRepo.GetJobNameAsync(sourceJobId, ct) ?? "";
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct) ?? "";
        var sourcePattern = await _autoBuildRepo.ExtractPatternAsync(sourceJobId, ct);

        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(sourceJobId, ct);
        var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);

        // Filter inactive agegroups from current
        var activeCurrentDivisions = currentDivisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Distinct agegroups with their division names — sort DESCENDING
        // so highest year claims its exact match first, preventing +1 overlaps
        var sourceAgegroups = sourceDivisions
            .GroupBy(d => d.AgegroupName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Name = g.Key, Divisions = g.Select(d => d.DivName).Distinct().OrderBy(n => n).ToList() })
            .OrderByDescending(a => a.Name)
            .ToList();

        var currentAgNames = activeCurrentDivisions
            .Select(d => d.AgegroupName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        // Build agegroup name → color map from current divisions
        var currentAgColors = activeCurrentDivisions
            .Where(d => d.AgegroupColor != null)
            .GroupBy(d => d.AgegroupName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().AgegroupColor!, StringComparer.OrdinalIgnoreCase);

        var claimedCurrent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var proposals = new List<AgegroupMappingProposal>();
        foreach (var src in sourceAgegroups)
        {
            // Try year increment (+1) FIRST — grad-year agegroups (2029→2030, 2030→2031)
            // must claim their +1 target before exact match can steal it.
            // Descending sort ensures highest source claims first, preventing collisions.
            if (TryIncrementYear(src.Name, out var incremented))
            {
                var incrementMatch = currentAgNames.FirstOrDefault(n =>
                    string.Equals(n, incremented, StringComparison.OrdinalIgnoreCase));
                if (incrementMatch != null && !claimedCurrent.Contains(incrementMatch))
                {
                    claimedCurrent.Add(incrementMatch);
                    proposals.Add(new AgegroupMappingProposal
                    {
                        SourceAgegroupName = src.Name,
                        SourceDivisionNames = src.Divisions,
                        SourceDivisionCount = src.Divisions.Count,
                        ProposedCurrentAgegroupName = incrementMatch,
                        MatchStrategy = "year-increment"
                    });
                    continue;
                }
            }

            // Fallback: exact match (for non-year names like "Sixth Graders",
            // or when +1 target doesn't exist in current year)
            var exactMatch = currentAgNames.FirstOrDefault(n =>
                string.Equals(n, src.Name, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null && !claimedCurrent.Contains(exactMatch))
            {
                claimedCurrent.Add(exactMatch);
                proposals.Add(new AgegroupMappingProposal
                {
                    SourceAgegroupName = src.Name,
                    SourceDivisionNames = src.Divisions,
                    SourceDivisionCount = src.Divisions.Count,
                    ProposedCurrentAgegroupName = exactMatch,
                    MatchStrategy = "exact"
                });
                continue;
            }

            // No match
            proposals.Add(new AgegroupMappingProposal
            {
                SourceAgegroupName = src.Name,
                SourceDivisionNames = src.Divisions,
                SourceDivisionCount = src.Divisions.Count,
                ProposedCurrentAgegroupName = null,
                MatchStrategy = "none"
            });
        }

        // Re-sort ascending for display
        proposals = proposals.OrderBy(p => p.SourceAgegroupName).ToList();

        return new AgegroupMappingResponse
        {
            SourceJobId = sourceJobId,
            SourceJobName = sourceJobName,
            SourceYear = sourceYear,
            SourceTotalGames = sourcePattern.Count,
            Proposals = proposals,
            CurrentAgegroupNames = currentAgNames,
            CurrentAgegroupColors = currentAgColors
        };
    }

    /// <summary>
    /// Try to increment a 4-digit year (2020-2039) in the agegroup name.
    /// Handles: "2030" → "2031", "2028/2029" → "2029/2030", etc.
    /// </summary>
    private static bool TryIncrementYear(string name, out string incremented)
    {
        incremented = name;
        var matches = Regex.Matches(name, @"\b(20[2-3]\d)\b");
        if (matches.Count == 0) return false;

        var result = name;
        // Replace all year occurrences (for "2028/2029" → "2029/2030")
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var m = matches[i];
            if (int.TryParse(m.Value, out var yr))
                result = result[..m.Index] + (yr + 1) + result[(m.Index + m.Length)..];
        }

        incremented = result;
        return result != name;
    }

    // ══════════════════════════════════════════════════════════
    // Phase 2-3: Analysis + Feasibility
    // ══════════════════════════════════════════════════════════

    public async Task<AutoBuildAnalysisResponse> AnalyzeAsync(
        Guid jobId, Guid sourceJobId,
        List<ConfirmedAgegroupMapping>? agegroupMappings = null,
        CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        // 1. Get source info
        var sourceJobName = await _autoBuildRepo.GetJobNameAsync(sourceJobId, ct) ?? "";
        var sourceYear = await _autoBuildRepo.GetJobYearAsync(sourceJobId, ct) ?? "";
        var sourcePattern = await _autoBuildRepo.ExtractPatternAsync(sourceJobId, ct);

        // 2. Get division summaries from both sides
        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(sourceJobId, ct);
        var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);

        // 3. Build source lookups
        var sourceDivTeamCount = sourceDivisions
            .ToDictionary(d => (d.AgegroupName, d.DivName), d => d.TeamCount);

        // RR-only source patterns indexed by (AgegroupName, DivName)
        var rrPatterns = sourcePattern
            .Where(p => p.T1Type == "T" && p.T2Type == "T")
            .ToList();

        var patternByName = rrPatterns
            .GroupBy(p => (p.AgegroupName, p.DivName))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Pool-size fallback: TeamCount → representative patterns (first alpha)
        var patternByPoolSize = rrPatterns
            .Where(p => sourceDivTeamCount.ContainsKey((p.AgegroupName, p.DivName)))
            .GroupBy(p => sourceDivTeamCount[(p.AgegroupName, p.DivName)])
            .ToDictionary(g => g.Key, g =>
            {
                var firstDiv = g
                    .GroupBy(p => (p.AgegroupName, p.DivName))
                    .OrderBy(sg => sg.Key.AgegroupName)
                    .ThenBy(sg => sg.Key.DivName)
                    .First();
                return firstDiv.ToList();
            });

        var availablePatterns = patternByPoolSize
            .Select(kvp => new PoolSizePattern
            {
                TeamCount = kvp.Key,
                GameCount = kvp.Value.Count,
                SourceDivisionCount = sourceDivisions.Count(d => d.TeamCount == kvp.Key)
            })
            .OrderBy(p => p.TeamCount)
            .ToList();

        // 4. Build reverse agegroup mapping: currentAgName → sourceAgName
        var currentToSourceAg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (agegroupMappings != null)
        {
            foreach (var m in agegroupMappings.Where(m => m.CurrentAgegroupName != null))
                currentToSourceAg.TryAdd(m.CurrentAgegroupName!, m.SourceAgegroupName);
        }

        // Build agegroup color lookup from current divisions
        var agColorLookup = currentDivisions
            .Where(d => d.AgegroupColor != null)
            .GroupBy(d => d.AgegroupName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().AgegroupColor!, StringComparer.OrdinalIgnoreCase);

        // 5. Assess coverage with name-first matching
        var activeDivisions = currentDivisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var divisionCoverage = activeDivisions.Select(d =>
        {
            // Try name-first: if this agegroup is mapped and source has same div name
            if (currentToSourceAg.TryGetValue(d.AgegroupName, out var sourceAgName))
            {
                var nameKey = (sourceAgName, d.DivName);
                if (patternByName.ContainsKey(nameKey))
                {
                    var sourceTeamCount = sourceDivTeamCount.GetValueOrDefault(nameKey);
                    if (sourceTeamCount == d.TeamCount)
                    {
                        // Same name + same pool size → name-matched
                        return new PoolSizeCoverage
                        {
                            DivId = d.DivId, AgegroupId = d.AgegroupId,
                            AgegroupName = d.AgegroupName,
                            AgegroupColor = agColorLookup.GetValueOrDefault(d.AgegroupName),
                            DivName = d.DivName,
                            TeamCount = d.TeamCount, HasPattern = true,
                            PatternGameCount = patternByName[nameKey].Count,
                            MatchStrategy = "name-matched",
                            SourceAgegroupName = sourceAgName, SourceDivName = d.DivName
                        };
                    }
                    // Name exists but pool size differs → fall through to pool-size
                }
            }

            // Pool-size fallback
            if (patternByPoolSize.ContainsKey(d.TeamCount))
            {
                return new PoolSizeCoverage
                {
                    DivId = d.DivId, AgegroupId = d.AgegroupId,
                    AgegroupName = d.AgegroupName,
                    AgegroupColor = agColorLookup.GetValueOrDefault(d.AgegroupName),
                    DivName = d.DivName,
                    TeamCount = d.TeamCount, HasPattern = true,
                    PatternGameCount = patternByPoolSize[d.TeamCount].Count,
                    MatchStrategy = "pool-size-fallback",
                    SourceAgegroupName = null, SourceDivName = null
                };
            }

            // No match
            return new PoolSizeCoverage
            {
                DivId = d.DivId, AgegroupId = d.AgegroupId,
                AgegroupName = d.AgegroupName,
                AgegroupColor = agColorLookup.GetValueOrDefault(d.AgegroupName),
                DivName = d.DivName,
                TeamCount = d.TeamCount, HasPattern = false,
                PatternGameCount = 0, MatchStrategy = "no-match",
                SourceAgegroupName = null, SourceDivName = null
            };
        }).ToList();

        // 5. Check field availability (name-based first, then address-based fallback)
        var sourceFieldNames = await _autoBuildRepo.GetSourceFieldNamesAsync(sourceJobId, ct);
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var currentFieldNames = currentFields.Select(f => f.FName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nameUnmatched = sourceFieldNames
            .Where(sf => !currentFieldNames.Contains(sf))
            .ToList();

        var addressMatched = new List<string>();
        var fieldMismatches = new List<string>();

        if (nameUnmatched.Count > 0)
        {
            var sourceFieldIds = sourcePattern
                .Where(p => nameUnmatched.Contains(p.FieldName, StringComparer.OrdinalIgnoreCase))
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

            var addressToCurrentName = new Dictionary<string, string>();
            foreach (var cf in currentFields)
            {
                if (currentAddresses.TryGetValue(cf.FieldId, out var addr))
                    addressToCurrentName.TryAdd(addr, cf.FName);
            }

            foreach (var unmatchedName in nameUnmatched)
            {
                var matchingSourceId = sourcePattern
                    .FirstOrDefault(p => string.Equals(p.FieldName, unmatchedName, StringComparison.OrdinalIgnoreCase))
                    ?.FieldId;

                if (matchingSourceId.HasValue
                    && matchingSourceId.Value != Guid.Empty
                    && sourceAddresses.TryGetValue(matchingSourceId.Value, out var srcAddr)
                    && addressToCurrentName.TryGetValue(srcAddr, out var currentName))
                {
                    addressMatched.Add($"{unmatchedName} → {currentName} (same address)");
                }
                else
                {
                    fieldMismatches.Add(unmatchedName);
                }
            }
        }

        // 7. Compute feasibility
        var totalCurrent = divisionCoverage.Count;
        var covered = divisionCoverage.Count(c => c.HasPattern);
        var uncovered = divisionCoverage.Count(c => !c.HasPattern);
        var nameMatched = divisionCoverage.Count(c => c.MatchStrategy == "name-matched");

        var confidencePercent = totalCurrent > 0
            ? (int)Math.Round(100.0 * covered / totalCurrent)
            : 0;

        var confidenceLevel = confidencePercent switch
        {
            > 80 => "green",
            > 50 => "yellow",
            _ => "red"
        };

        var warnings = new List<string>();
        if (addressMatched.Count > 0)
            warnings.Add($"Fields matched by address (renamed): {string.Join("; ", addressMatched)}");

        return new AutoBuildAnalysisResponse
        {
            SourceJobId = sourceJobId,
            SourceJobName = sourceJobName,
            SourceYear = sourceYear,
            SourceTotalGames = sourcePattern.Count,
            DivisionCoverage = divisionCoverage,
            AgegroupMappings = agegroupMappings ?? [],
            Feasibility = new AutoBuildFeasibility
            {
                TotalCurrentDivisions = totalCurrent,
                CoveredDivisions = covered,
                UncoveredDivisions = uncovered,
                NameMatchedDivisions = nameMatched,
                ConfidenceLevel = confidenceLevel,
                ConfidencePercent = confidencePercent,
                FieldMismatches = fieldMismatches,
                Warnings = warnings,
                AvailablePatterns = availablePatterns
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

        // 1. Extract the source pattern and build lookups
        var pattern = await _autoBuildRepo.ExtractPatternAsync(request.SourceJobId, ct);
        var sourceDivisions = await _autoBuildRepo.GetSourceDivisionSummariesAsync(request.SourceJobId, ct);

        var sourceDivTeamCount = sourceDivisions
            .ToDictionary(d => (d.AgegroupName, d.DivName), d => d.TeamCount);

        var rrPatterns = pattern
            .Where(p => p.T1Type == "T" && p.T2Type == "T")
            .ToList();

        // Name-based: (sourceAg, divName) → patterns
        var patternByName = rrPatterns
            .GroupBy(p => (p.AgegroupName, p.DivName))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Pool-size fallback: TeamCount → representative patterns
        var patternByPoolSize = rrPatterns
            .Where(p => sourceDivTeamCount.ContainsKey((p.AgegroupName, p.DivName)))
            .GroupBy(p => sourceDivTeamCount[(p.AgegroupName, p.DivName)])
            .ToDictionary(g => g.Key, g =>
            {
                var firstDiv = g
                    .GroupBy(p => (p.AgegroupName, p.DivName))
                    .OrderBy(sg => sg.Key.AgegroupName)
                    .ThenBy(sg => sg.Key.DivName)
                    .First();
                return firstDiv.ToList();
            });

        // Build reverse agegroup mapping: currentAgName → sourceAgName
        var currentToSourceAg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.AgegroupMappings != null)
        {
            foreach (var m in request.AgegroupMappings.Where(m => m.CurrentAgegroupName != null))
                currentToSourceAg.TryAdd(m.CurrentAgegroupName!, m.SourceAgegroupName);
        }

        // 2. Get current divisions
        var currentDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var activeDivisions = currentDivisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.AgegroupName)
            .ThenBy(d => d.DivName)
            .ToList();

        // 3. Get current timeslot dates cache (keyed by divId — agegroups can have div-specific timeslots)
        var currentDatesByDiv = new Dictionary<Guid, List<DateTime>>();

        // 4. Get current fields for name matching + address-based fallback
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var fieldNameToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in currentFields)
            fieldNameToId.TryAdd(f.FName, f.FieldId);

        // Address-based fallback
        var sourceFieldIds = pattern
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

        var addressToCurrentField = new Dictionary<string, (Guid fieldId, string fName)>();
        foreach (var cf in currentFields)
        {
            if (currentAddresses.TryGetValue(cf.FieldId, out var addr))
                addressToCurrentField.TryAdd(addr, (cf.FieldId, cf.FName));
        }

        var sourceFieldIdToCurrentFieldId = new Dictionary<Guid, Guid>();
        foreach (var (srcId, srcAddr) in sourceAddresses)
        {
            if (addressToCurrentField.TryGetValue(srcAddr, out var currentField))
                sourceFieldIdToCurrentFieldId[srcId] = currentField.fieldId;
        }

        foreach (var p in pattern)
        {
            if (!fieldNameToId.ContainsKey(p.FieldName)
                && sourceFieldIdToCurrentFieldId.TryGetValue(p.FieldId, out var currentFieldId))
            {
                fieldNameToId[p.FieldName] = currentFieldId;
            }
        }

        var fieldIdToName = currentFields
            .GroupBy(f => f.FieldId)
            .ToDictionary(g => g.Key, g => g.First().FName);

        // 5. Get skip set from user input
        var skipIds = request.SkipDivisionIds?.ToHashSet() ?? [];

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

        // BTB avoidance: track team game times across all divisions
        var btbTracker = new BtbTracker();

        // 8. Process each current division by pool size
        foreach (var div in activeDivisions)
        {
            var divId = div.DivId;
            var agegroupId = div.AgegroupId;

            if (skipIds.Contains(divId))
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName,
                    DivName = div.DivName,
                    DivId = divId,
                    GamesPlaced = 0,
                    GamesFailed = 0,
                    Status = "skipped"
                });
                continue;
            }

            if (request.SkipAlreadyScheduled && existingCounts.GetValueOrDefault(divId) > 0)
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName,
                    DivName = div.DivName,
                    DivId = divId,
                    GamesPlaced = 0,
                    GamesFailed = 0,
                    Status = "already-scheduled"
                });
                continue;
            }

            // Delete existing games for this division before rebuilding
            if (existingCounts.GetValueOrDefault(divId) > 0)
            {
                await _scheduleRepo.DeleteDivisionGamesAsync(divId, leagueId, season, year, ct);
                await _scheduleRepo.SaveChangesAsync(ct);
            }

            // Determine strategy: name-first → pool-size fallback → auto-schedule
            List<GamePlacementPattern>? placements = null;
            var status = "auto-schedule";

            // Try name-matched pattern first
            if (currentToSourceAg.TryGetValue(div.AgegroupName, out var sourceAgName))
            {
                var nameKey = (sourceAgName, div.DivName);
                if (patternByName.TryGetValue(nameKey, out var namePatterns))
                {
                    var sourceTeamCount = sourceDivTeamCount.GetValueOrDefault(nameKey);
                    if (sourceTeamCount == div.TeamCount)
                    {
                        placements = namePatterns;
                        status = "pattern-replay-name";
                    }
                    // else: name exists but pool size changed → fall through
                }
            }

            // Pool-size fallback
            if (placements == null && patternByPoolSize.TryGetValue(div.TeamCount, out var poolPatterns))
            {
                placements = poolPatterns;
                status = "pattern-replay";
            }

            if (placements == null)
            {
                // Auto-schedule fallback
                var result = await _scheduleDivisionService.AutoScheduleDivAsync(jobId, userId, divId, ct);

                if (result.ScheduledCount > 0)
                {
                    var newOccupied = await _scheduleRepo.GetOccupiedSlotsAsync(jobId, allFieldIds, ct);
                    occupiedSlots = newOccupied;
                }

                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName,
                    DivName = div.DivName,
                    DivId = divId,
                    GamesPlaced = result.ScheduledCount,
                    GamesFailed = result.FailedCount,
                    Status = "auto-schedule"
                });
                totalPlaced += result.ScheduledCount;
                totalFailed += result.FailedCount;
            }
            else
            {
                // Pattern replay (name-matched or pool-size)
                var (placed, failed) = await ReplayPatternForDivisionAsync(
                    jobId, userId, leagueId, season, year,
                    agegroupId, divId, div.AgegroupName, div.DivName,
                    placements, fieldNameToId, fieldIdToName,
                    currentDatesByDiv, occupiedSlots, btbTracker,
                    request.IncludeBracketGames, ct);

                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName,
                    DivName = div.DivName,
                    DivId = divId,
                    GamesPlaced = placed,
                    GamesFailed = failed,
                    Status = status
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
            jobId, activeDivisions.Count, scheduled, skipped, totalPlaced, totalFailed);

        return new AutoBuildResult
        {
            TotalDivisions = activeDivisions.Count,
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
    // V2: Prerequisite Checks
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
    // V2: Profile Extraction (Q1–Q10)
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
                .ToList()
        };
    }

    // ══════════════════════════════════════════════════════════
    // V2: Build — Horizontal-First Placement with Scoring Engine
    // ══════════════════════════════════════════════════════════

    public async Task<AutoBuildV2Result> BuildV2Async(
        Guid jobId, string userId, AutoBuildV2Request request, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // ── 1. Extract profiles ──
        Dictionary<int, DivisionSizeProfile> profilesByTCnt;
        Dictionary<DayOfWeek, TimeSpan>? sourceTimeslotWindow = null;
        if (request.SourceJobId.HasValue)
        {
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
            // Clean sheet mode — profiles built later per-division from timeslot config
            profilesByTCnt = new Dictionary<int, DivisionSizeProfile>();
        }

        // ── 2. Build ranked constraint evaluators ──
        var allEvaluators = new Dictionary<string, IConstraintEvaluator>(StringComparer.OrdinalIgnoreCase)
        {
            ["correct-day"] = new ConstraintEvaluators.CorrectDayEvaluator(),
            ["placement-shape"] = new ConstraintEvaluators.PlacementShapeEvaluator(),
            ["onsite-window"] = new ConstraintEvaluators.OnsiteWindowEvaluator(),
            ["field-assignment"] = new ConstraintEvaluators.FieldAssignmentEvaluator(),
            ["field-distribution"] = new ConstraintEvaluators.FieldDistributionEvaluator()
        };

        var rankedConstraints = PlacementScorer.BuildRankedConstraints(
            request.ConstraintPriorities, allEvaluators);

        // ── 3. Get current divisions and filter ──
        var allDivisions = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var activeDivisions = allDivisions
            .Where(d => !d.AgegroupName.StartsWith("WAITLIST", StringComparison.OrdinalIgnoreCase)
                     && !d.AgegroupName.StartsWith("DROPPED", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(d.DivName, "Unassigned", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var excludedIds = request.ExcludedDivisionIds.ToHashSet();

        // ── 4. Initialize global state ──
        var currentFields = await _autoBuildRepo.GetCurrentFieldsAsync(leagueId, season, ct);
        var allFieldIds = currentFields.Select(f => f.FieldId).ToList();
        var fieldIdToName = currentFields
            .GroupBy(f => f.FieldId)
            .ToDictionary(g => g.Key, g => g.First().FName);

        var occupiedSlots = allFieldIds.Count > 0
            ? await _scheduleRepo.GetOccupiedSlotsAsync(jobId, allFieldIds, ct)
            : new HashSet<(Guid fieldId, DateTime gDate)>();

        var btbTracker = new BtbTracker();
        var btbThreshold = 0;

        var existingCounts = await _autoBuildRepo.GetExistingGameCountsByDivisionAsync(jobId, ct);

        // ── 5. Build processing order: agegroups → divisions ──
        var orderedDivisions = BuildProcessingOrder(
            activeDivisions, request.AgegroupOrder, request.DivisionOrderStrategy, excludedIds);

        // ── 6. Process each division ──
        var divisionResults = new List<AutoBuildDivisionResult>();
        var unplacedGames = new List<UnplacedGameDto>();
        var sacrificeCounts = new Dictionary<string, (int Count, List<string> Examples)>();
        var totalPlaced = 0;
        var totalFailed = 0;

        foreach (var div in orderedDivisions)
        {
            if (excludedIds.Contains(div.DivId))
            {
                divisionResults.Add(new AutoBuildDivisionResult
                {
                    AgegroupName = div.AgegroupName, DivName = div.DivName,
                    DivId = div.DivId, GamesPlaced = 0, GamesFailed = 0, Status = "excluded"
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
                    DivId = div.DivId, GamesPlaced = 0, GamesFailed = 0,
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
                    DivId = div.DivId, GamesPlaced = 0, GamesFailed = 0,
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
                    DivId = div.DivId, GamesPlaced = 0, GamesFailed = missingCount,
                    Status = "no-timeslots"
                });
                continue;
            }

            // Compute BTB threshold from field config
            btbThreshold = effectiveFields.Count > 0
                ? effectiveFields.Max(f => f.GamestartInterval) : 0;

            // Build candidate slots: date × field × game intervals
            var candidates = GenerateCandidateSlots(effectiveDates, effectiveFields);

            // Get or build profile for this TCnt
            var profile = GetOrBuildDefaultProfile(
                profilesByTCnt, teamCount, effectiveDates, effectiveFields);

            // Remap source play days to current play days by ordinal position
            var currentPlayDays = effectiveDates
                .Select(d => d.GDate.DayOfWeek)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            profile = ApplyDayOrdinalMap(profile, currentPlayDays);

            // Compute current job's timeslot window start per DOW from field config
            var currentWindowStart = new Dictionary<DayOfWeek, TimeSpan>();
            foreach (var field in effectiveFields)
            {
                if (!TimeSpan.TryParse(field.StartTime, out var st)) continue;
                if (!Enum.TryParse<DayOfWeek>(field.Dow, true, out var dow)) continue;
                if (!currentWindowStart.TryGetValue(dow, out var existing) || st < existing)
                    currentWindowStart[dow] = st;
            }

            // Build PlacementState for this pass
            var state = new PlacementState(occupiedSlots, btbTracker, btbThreshold, currentWindowStart);

            // Group pairings by round
            var rounds = rrPairings
                .GroupBy(p => p.Rnd)
                .OrderBy(g => g.Key)
                .ToList();

            var divPlaced = 0;
            var divFailed = 0;

            foreach (var round in rounds)
            {
                var roundNum = round.Key;
                var gamesInRound = round.OrderBy(p => p.GameNumber).ToList();

                foreach (var pairing in gamesInRound)
                {
                    // Build game context with target day/time from profile
                    DayOfWeek? targetDay = null;
                    if (profile.PlayDays.Count > 0)
                    {
                        // Distribute rounds across play days
                        var dayIndex = (roundNum - 1) % profile.PlayDays.Count;
                        targetDay = profile.PlayDays[dayIndex];
                    }

                    // Target time: round target (first game sets it) → offset-based → fallback to null
                    TimeSpan? targetTime = null;
                    var roundKey = (div.DivId, roundNum);
                    if (state.RoundTargetTimes.TryGetValue(roundKey, out var existingTarget))
                    {
                        targetTime = existingTarget;
                    }
                    else if (targetDay.HasValue
                             && profile.StartOffsetFromWindow != null
                             && profile.StartOffsetFromWindow.TryGetValue(targetDay.Value, out var offset)
                             && currentWindowStart.TryGetValue(targetDay.Value, out var winStart))
                    {
                        // Offset-based: current window start + source offset = where to aim
                        targetTime = winStart + offset;
                    }

                    var game = new GameContext
                    {
                        Round = roundNum,
                        GameNumber = pairing.GameNumber,
                        T1No = pairing.T1,
                        T2No = pairing.T2,
                        DivId = div.DivId,
                        AgegroupId = div.AgegroupId,
                        AgegroupName = div.AgegroupName,
                        DivName = div.DivName,
                        TCnt = teamCount,
                        TargetDay = targetDay,
                        TargetTime = targetTime
                    };

                    // Score all candidates and find best
                    var best = PlacementScorer.FindBestSlot(
                        candidates, game, profile, rankedConstraints, state);

                    if (best == null)
                    {
                        divFailed++;
                        unplacedGames.Add(new UnplacedGameDto
                        {
                            AgegroupName = div.AgegroupName,
                            DivName = div.DivName,
                            Round = roundNum,
                            T1No = pairing.T1,
                            T2No = pairing.T2,
                            Reason = "No available slot (all occupied or BTB conflict)"
                        });
                        continue;
                    }

                    // Track constraint sacrifices
                    if (best.Violations.Count > 0)
                    {
                        foreach (var violation in best.Violations)
                        {
                            if (!sacrificeCounts.TryGetValue(violation, out var entry))
                            {
                                entry = (0, new List<string>());
                                sacrificeCounts[violation] = entry;
                            }
                            var count = entry.Count + 1;
                            var examples = entry.Examples;
                            if (examples.Count < 3)
                            {
                                examples.Add(
                                    $"{div.AgegroupName}/{div.DivName} R{roundNum} #{pairing.T1}v#{pairing.T2}");
                            }
                            sacrificeCounts[violation] = (count, examples);
                        }
                    }

                    // Create the Schedule entity
                    var scheduleGame = new Schedule
                    {
                        JobId = jobId,
                        LeagueId = agLeagueId,
                        Season = agSeason,
                        Year = year,
                        AgegroupId = div.AgegroupId,
                        AgegroupName = agegroup?.AgegroupName ?? "",
                        DivId = div.DivId,
                        DivName = division?.DivName ?? "",
                        Div2Id = div.DivId,
                        FieldId = best.Slot.FieldId,
                        FName = best.Slot.FieldName,
                        GDate = best.Slot.GDate,
                        GNo = pairing.GameNumber,
                        GStatusCode = 1, // Scheduled
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
                    state.RecordPlacement(best.Slot, game);
                    divPlaced++;
                }
            }

            // Bulk save per division
            if (divPlaced > 0)
                await _scheduleRepo.SaveChangesAsync(ct);

            // Resolve team names
            await _scheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(
                div.DivId, jobId, ct);

            divisionResults.Add(new AutoBuildDivisionResult
            {
                AgegroupName = div.AgegroupName,
                DivName = div.DivName,
                DivId = div.DivId,
                GamesPlaced = divPlaced,
                GamesFailed = divFailed,
                Status = divPlaced > 0 ? "v2-placed" : "no-slots"
            });

            totalPlaced += divPlaced;
            totalFailed += divFailed;
        }

        // ── 7. Build sacrifice log ──
        var sacrificeLog = sacrificeCounts
            .Select(kvp => new ConstraintSacrificeDto
            {
                ConstraintName = kvp.Key,
                ViolationCount = kvp.Value.Count,
                ExampleGames = kvp.Value.Examples,
                ImpactDescription = allEvaluators.TryGetValue(kvp.Key, out var eval)
                    ? eval.SacrificeImpact
                    : ""
            })
            .OrderByDescending(s => s.ViolationCount)
            .ToList();

        var scheduled = divisionResults.Count(r =>
            r.Status != "excluded" && r.Status != "no-teams" && r.Status != "no-pairings");
        var skipped = divisionResults.Count - scheduled;

        _logger.LogInformation(
            "AutoBuild V2: Job={JobId}, Divisions={Total}, Scheduled={Scheduled}, " +
            "GamesPlaced={Placed}, GamesFailed={Failed}, Sacrifices={SacrificeCount}",
            jobId, orderedDivisions.Count, scheduled, totalPlaced, totalFailed, sacrificeLog.Count);

        return new AutoBuildV2Result
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
    // V2 Private: Processing Order
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
    // V2 Private: Candidate Slot Generation
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

        return candidates;
    }

    // ══════════════════════════════════════════════════════════
    // V2 Private: Default Profile for Clean Sheet
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Get a profile from extracted profiles, or build a default from timeslot config.
    /// Clean sheet mode: no prior year data, so derive sensible defaults.
    /// </summary>
    private static DivisionSizeProfile GetOrBuildDefaultProfile(
        Dictionary<int, DivisionSizeProfile> profilesByTCnt,
        int teamCount,
        List<TimeslotDateDto> dates,
        List<TimeslotFieldDto> fields)
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
        var defaultInterval = fields.Count > 0
            ? TimeSpan.FromMinutes(fields.Max(f => f.GamestartInterval))
            : TimeSpan.FromMinutes(75);

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
            InterRoundInterval = defaultInterval
        };

        // Cache for subsequent divisions with same TCnt
        profilesByTCnt[teamCount] = defaultProfile;
        return defaultProfile;
    }

    // ══════════════════════════════════════════════════════════
    // V2 Private: Source Timeslot Window
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
    // V2 Private: Field Name Mapping (Source → Current)
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
    // Private: Pattern Replay for a Single Division
    // ══════════════════════════════════════════════════════════

    private async Task<(int placed, int failed)> ReplayPatternForDivisionAsync(
        Guid jobId, string userId, Guid leagueId, string season, string year,
        Guid agegroupId, Guid divId, string agegroupName, string divName,
        List<GamePlacementPattern> allPlacements,
        Dictionary<string, Guid> fieldNameToId,
        Dictionary<Guid, string> fieldIdToName,
        Dictionary<Guid, List<DateTime>> currentDatesByDiv,
        HashSet<(Guid fieldId, DateTime gDate)> occupiedSlots,
        BtbTracker btbTracker,
        bool includeBracketGames,
        CancellationToken ct)
    {
        // Filter to round-robin only unless bracket games requested
        var placements = includeBracketGames
            ? allPlacements
            : allPlacements.Where(p => p.T1Type == "T" && p.T2Type == "T").ToList();

        if (placements.Count == 0)
            return (0, 0);

        // Get current year's timeslot dates for this division
        // Keyed by divId because timeslots can be division-specific within an agegroup
        if (!currentDatesByDiv.TryGetValue(divId, out var currentDates))
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

            currentDatesByDiv[divId] = currentDates;
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
                var fallbackSlot = TimeslotSlotFinder.FindNextAvailable(effectiveDatesForFallback, effectiveFields, occupiedSlots);
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
                var fallbackSlot = TimeslotSlotFinder.FindNextAvailable(effectiveDatesForFallback, effectiveFields, occupiedSlots);
                if (fallbackSlot == null) { failed++; continue; }

                targetFieldId = fallbackSlot.Value.fieldId;
                targetDate = fallbackSlot.Value.gDate.Date;
            }

            // Construct the target game datetime
            var targetGDate = targetDate + placement.TimeOfDay;

            // Resolve team numbers for BTB checking
            var t1No = pairing?.T1 ?? 0;
            var t2No = pairing?.T2 ?? 0;

            // Check if slot is occupied OR would cause a back-to-back
            var needsFallback = occupiedSlots.Contains((targetFieldId, targetGDate));
            if (!needsFallback && t1No > 0 && t2No > 0)
            {
                var btbThreshold = effectiveFields.Count > 0
                    ? effectiveFields.Max(f => f.GamestartInterval) : 0;
                if (btbThreshold > 0 && btbTracker.HasConflict(divId, t1No, t2No, targetGDate, btbThreshold))
                    needsFallback = true;
            }

            if (needsFallback)
            {
                // Try fallback with BTB awareness
                var fallbackSlot = TimeslotSlotFinder.FindNextAvailable(
                    effectiveDatesForFallback, effectiveFields, occupiedSlots,
                    btbTracker, divId, t1No, t2No);
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
                T1No = t1No,
                T1Type = pairing?.T1Type ?? placement.T1Type,
                T2No = (byte)t2No,
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
            occupiedSlots.Add((targetFieldId, targetGDate));

            // Record in BTB tracker so subsequent placements avoid this team's time
            if (t1No > 0) btbTracker.Record(divId, t1No, targetGDate);
            if (t2No > 0) btbTracker.Record(divId, t2No, targetGDate);

            placed++;
        }

        // Single save for all games in this division (was per-game before)
        if (placed > 0)
            await _scheduleRepo.SaveChangesAsync(ct);

        // Bulk resolve team names for the entire division
        await _scheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(divId, jobId, ct);

        return (placed, failed);
    }

}
