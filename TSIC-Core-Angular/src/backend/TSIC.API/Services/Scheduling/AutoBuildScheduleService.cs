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

        var fieldIdToName = currentFields.ToDictionary(f => f.FieldId, f => f.FName);

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
                    currentDatesByDiv, occupiedSlots,
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

            // Check if slot is occupied
            if (occupiedSlots.Contains((targetFieldId, targetGDate)))
            {
                // Try fallback
                var fallbackSlot = TimeslotSlotFinder.FindNextAvailable(effectiveDatesForFallback, effectiveFields, occupiedSlots);
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
            occupiedSlots.Add((targetFieldId, targetGDate));
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
