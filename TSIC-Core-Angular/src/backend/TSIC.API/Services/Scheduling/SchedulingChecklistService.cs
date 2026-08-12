using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Computes the ordered scheduling checklist. Strictly read-only: it reads raw config
/// (no cascade self-heal, no bracket-seed resolution) so a GET never mutates state.
/// The step definitions mirror the build gate in <see cref="AutoBuildScheduleService"/> —
/// if a step is green here, the corresponding prerequisite passes there.
/// </summary>
public sealed class SchedulingChecklistService : ISchedulingChecklistService
{
    private readonly IAutoBuildRepository _autoBuildRepo;
    private readonly IPairingsRepository _pairingsRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IScheduleCascadeRepository _cascadeRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly ITimeslotRepository _timeslotRepo;
    private readonly IBracketRepository _bracketRepo;
    private readonly IBracketSeedRepository _bracketSeedRepo;
    private readonly IFieldRepository _fieldRepo;
    private readonly ISchedulingContextResolver _contextResolver;

    public SchedulingChecklistService(
        IAutoBuildRepository autoBuildRepo,
        IPairingsRepository pairingsRepo,
        IScheduleRepository scheduleRepo,
        IScheduleCascadeRepository cascadeRepo,
        ITeamRepository teamRepo,
        ITimeslotRepository timeslotRepo,
        IBracketRepository bracketRepo,
        IBracketSeedRepository bracketSeedRepo,
        IFieldRepository fieldRepo,
        ISchedulingContextResolver contextResolver)
    {
        _autoBuildRepo = autoBuildRepo;
        _pairingsRepo = pairingsRepo;
        _scheduleRepo = scheduleRepo;
        _cascadeRepo = cascadeRepo;
        _teamRepo = teamRepo;
        _timeslotRepo = timeslotRepo;
        _bracketRepo = bracketRepo;
        _bracketSeedRepo = bracketSeedRepo;
        _fieldRepo = fieldRepo;
        _contextResolver = contextResolver;
    }

    public async Task<SchedulingChecklistDto> GetChecklistAsync(Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Sequential queries — repositories share a single scoped DbContext
        var agegroups = await _pairingsRepo.GetAgegroupsWithDivisionsAsync(leagueId, season, ct);
        var teamCountsByAg = await _teamRepo.GetRegistrationCountsByAgeGroupAsync(jobId, ct);
        var teamCountsByDiv = await _teamRepo.GetTeamCountsByDivisionAsync(jobId, ct);
        var readinessByAg = await _timeslotRepo.GetReadinessDataAsync(leagueId, season, year, ct);
        var divisionSummaries = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var poolSizesWithPairings = await _pairingsRepo.GetDistinctPoolSizesWithPairingsAsync(leagueId, season, ct);
        var eventDefaults = await _cascadeRepo.GetEventDefaultsAsync(jobId, ct);
        var agProfiles = await _cascadeRepo.GetAgegroupProfilesAsync(jobId, ct);
        var divProfiles = await _cascadeRepo.GetDivisionProfilesAsync(jobId, ct);
        var scheduleStats = await _scheduleRepo.GetScheduleSummaryStatsAsync(jobId, ct);
        var activeFieldCount = await _fieldRepo.CountActiveLeagueSeasonFieldsAsync(leagueId, season, ct);

        // ── Step 0: Fields set up for the season ──
        // ≥1 active field assigned to the league-season. Zero-numbered prerequisite (Todd,
        // 08-12): fields depend on nothing else and timeslots are authored against them.
        var fieldsSetup = new ChecklistFieldsSetupStepDto
        {
            Complete = activeFieldCount > 0,
            ActiveFieldCount = activeFieldCount
        };

        // Utility agegroups (WAITLIST-*, Dropped Teams) and empty agegroups never gate anything
        var relevantAgegroups = agegroups
            .Where(ag => IsSchedulableAgegroup(ag.AgegroupName))
            .Where(ag => teamCountsByAg.TryGetValue(ag.AgegroupId, out var cnt) && cnt > 0)
            .OrderBy(ag => ag.AgegroupName)
            .ToList();

        // ── Step 1: Pools ──
        // Unpooled = active teams not sitting in a real (non-"Unassigned") division.
        // Computed as total minus assigned so teams with a NULL DivId count as unpooled too.
        var poolOffenders = new List<ChecklistAgegroupPoolsDto>();
        foreach (var ag in relevantAgegroups)
        {
            var total = teamCountsByAg[ag.AgegroupId];
            var assigned = (ag.Divisions ?? [])
                .Where(d => !string.Equals(d.DivName, DivisionConstants.Unassigned, StringComparison.OrdinalIgnoreCase))
                .Sum(d => teamCountsByDiv.TryGetValue(d.DivId, out var c) ? c : 0);
            var unpooled = total - assigned;
            if (unpooled > 0)
            {
                poolOffenders.Add(new ChecklistAgegroupPoolsDto
                {
                    AgegroupId = ag.AgegroupId,
                    AgegroupName = ag.AgegroupName ?? "",
                    UnpooledCount = unpooled,
                    TeamCount = total
                });
            }
        }
        var pools = new ChecklistPoolsStepDto
        {
            Complete = poolOffenders.Count == 0,
            TotalUnpooledTeams = poolOffenders.Sum(o => o.UnpooledCount),
            Offenders = poolOffenders
        };

        // ── Step 3: Assign Timeslots ──
        // Two grains in one step, matching how the config is authored and how the engine
        // resolves it: play dates belong to the agegroup, field timeslots to the division.
        // Checking fields per agegroup let one configured pool pass the whole agegroup while
        // the build placed nothing for its siblings.
        var schedulableDivisions = AutoBuildScheduleService.FilterSchedulableDivisions(divisionSummaries);

        var missingDates = relevantAgegroups
            .Where(ag => (readinessByAg.TryGetValue(ag.AgegroupId, out var r) ? r.DateCount : 0) == 0)
            .Select(ag => ag.AgegroupName ?? "")
            .ToList();
        var dates = new ChecklistAgegroupStepDto
        {
            Complete = missingDates.Count == 0,
            MissingAgegroups = missingDates
        };

        var fieldIdsPerAgegroup = await _timeslotRepo.GetFieldIdsPerAgegroupAsync(leagueId, season, year, ct);
        var fieldIdsPerDivision = await _timeslotRepo.GetFieldIdsPerDivisionAsync(leagueId, season, year, ct);

        var missingFieldsByAgegroup = AutoBuildScheduleService
            .FilterDivisionsMissingFields(schedulableDivisions, fieldIdsPerAgegroup, fieldIdsPerDivision)
            .GroupBy(d => d.AgegroupName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ChecklistAgegroupDivisionsDto
            {
                AgegroupName = g.Key,
                DivNames = g.Select(d => d.DivName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        var fields = new ChecklistDivisionStepDto
        {
            Complete = missingFieldsByAgegroup.Count == 0,
            MissingDivisionCount = missingFieldsByAgegroup.Sum(a => a.DivNames.Count),
            MissingByAgegroup = missingFieldsByAgegroup
        };

        // ── Step 4: Build rules (game guarantee) ──
        // Raw cascade reads only — the hub's Build Rules tab self-heals from pairings on visit,
        // and this status flipping green after that visit is exactly the desired behavior.
        var agGuarantee = agProfiles
            .Where(p => p.GameGuarantee.HasValue)
            .ToDictionary(p => p.AgegroupId, p => p.GameGuarantee!.Value);
        var divGuarantee = divProfiles
            .Where(p => p.GameGuarantee.HasValue)
            .ToDictionary(p => p.DivisionId, p => p.GameGuarantee!.Value);

        var divisionsWithoutGuarantee = schedulableDivisions
            .Where(d =>
            {
                var effective = divGuarantee.TryGetValue(d.DivId, out var dg) ? dg
                    : agGuarantee.TryGetValue(d.AgegroupId, out var ag) ? ag
                    : eventDefaults?.GameGuarantee ?? 0;
                return effective <= 0;
            })
            .Select(d => $"{d.AgegroupName} / {d.DivName}")
            .OrderBy(n => n)
            .ToList();
        var rules = new ChecklistRulesStepDto
        {
            Complete = divisionsWithoutGuarantee.Count == 0,
            DivisionsWithoutGuarantee = divisionsWithoutGuarantee
        };

        // ── Step 5: Pairings (informational — build auto-generates what's missing) ──
        var missingPoolSizes = schedulableDivisions
            .Where(d => d.TeamCount > 0)
            .Select(d => d.TeamCount)
            .Distinct()
            .Where(t => !poolSizesWithPairings.Contains(t))
            .OrderBy(t => t)
            .ToList();
        var pairings = new ChecklistPairingsStepDto
        {
            Complete = missingPoolSizes.Count == 0,
            MissingPoolSizes = missingPoolSizes
        };

        // ── Post-build step: Bracket Seeds ──
        // Coverage over the seeds-page universe (non-pool games; consolation included — it
        // seeds by the same path). A slot is covered by a director seed or an advancement
        // feed. Raw reads only: seed rows and the feed graph are materialized by the bracket
        // pages themselves, and this flipping green after those visits is the desired
        // behavior — same shape as the rules step above.
        var bracketSeeds = new ChecklistBracketStepDto
        {
            HasBracketGames = false,
            Complete = false,
            UncoveredSlotCount = 0,
            UncoveredByAgegroup = []
        };
        if (scheduleStats.GameCount > 0)
        {
            var bracketGames = await _bracketSeedRepo.GetBracketGamesAsync(jobId, ct);
            var fedSlots = (await _bracketRepo.GetFeedsByTargetJobAsync(jobId, ct))
                .Select(f => (f.TargetGid, (int)f.TargetSlot))
                .ToHashSet();

            var uncovered = new List<(string AgegroupName, string Type, int No)>();
            foreach (var g in bracketGames)
            {
                if (!(g.T1SeedDivId.HasValue && g.T1SeedRank.HasValue) && !fedSlots.Contains((g.Gid, 1)))
                    uncovered.Add((g.AgegroupName, g.T1Type, g.T1No));
                if (!(g.T2SeedDivId.HasValue && g.T2SeedRank.HasValue) && !fedSlots.Contains((g.Gid, 2)))
                    uncovered.Add((g.AgegroupName, g.T2Type, g.T2No));
            }

            bracketSeeds = new ChecklistBracketStepDto
            {
                HasBracketGames = bracketGames.Count > 0,
                Complete = uncovered.Count == 0,
                UncoveredSlotCount = uncovered.Count,
                UncoveredByAgegroup = uncovered
                    .GroupBy(u => u.AgegroupName)
                    .OrderBy(grp => grp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(grp => new ChecklistAgegroupSlotsDto
                    {
                        AgegroupName = grp.Key,
                        SlotLabels = grp
                            .OrderBy(u => RoundOrder.GetValueOrDefault(u.Type, 99))
                            .ThenBy(u => u.No)
                            .Select(u => $"{u.Type}{u.No}")
                            .ToList()
                    })
                    .ToList()
            };
        }

        return new SchedulingChecklistDto
        {
            FieldsSetup = fieldsSetup,
            Pools = pools,
            Dates = dates,
            Fields = fields,
            Rules = rules,
            Pairings = pairings,
            BracketSeeds = bracketSeeds,
            GameCount = scheduleStats.GameCount,
            BuildUnlocked = pools.Complete && dates.Complete && fields.Complete && rules.Complete,
            ScheduleStats = scheduleStats
        };
    }

    /// <summary>Earliest rounds first — the order the seeds page lists games.</summary>
    private static readonly Dictionary<string, int> RoundOrder = new()
    {
        ["Z"] = 0, ["Y"] = 1, ["X"] = 2, ["Q"] = 3, ["S"] = 4, ["F"] = 5, ["B"] = 6, ["C"] = 7
    };

    public async Task<ScheduleDashboardDto> GetDashboardAsync(Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await _contextResolver.ResolveAsync(jobId, ct);

        // Sequential queries — repositories share a single scoped DbContext
        var stats = await _scheduleRepo.GetScheduleSummaryStatsAsync(jobId, ct);
        var gamesPerDay = await _scheduleRepo.GetGameCountsByDayAsync(jobId, ct);
        var teamsScheduled = await _scheduleRepo.GetScheduledTeamCountAsync(jobId, ct);
        var divisionSummaries = await _autoBuildRepo.GetCurrentDivisionSummariesAsync(jobId, ct);
        var agegroups = await _pairingsRepo.GetAgegroupsWithDivisionsAsync(leagueId, season, ct);
        var teamCountsByAg = await _teamRepo.GetRegistrationCountsByAgeGroupAsync(jobId, ct);

        // Same filters the checklist gates on, so numerator and denominator share a universe:
        // a team the checklist would demand a pool for is a team the dashboard expects a game for.
        var activeTeamCount = agegroups
            .Where(ag => IsSchedulableAgegroup(ag.AgegroupName))
            .Sum(ag => teamCountsByAg.TryGetValue(ag.AgegroupId, out var cnt) ? cnt : 0);

        return new ScheduleDashboardDto
        {
            Stats = stats,
            SchedulableDivisionCount = AutoBuildScheduleService.FilterSchedulableDivisions(divisionSummaries).Count,
            TeamsScheduled = teamsScheduled,
            ActiveTeamCount = activeTeamCount,
            GamesPerDay = gamesPerDay
        };
    }

    /// <summary>Utility agegroups (WAITLIST-*, Dropped Teams) never gate or count anywhere.</summary>
    private static bool IsSchedulableAgegroup(string? agegroupName)
    {
        var name = (agegroupName ?? "").ToUpper();
        return name != "DROPPED TEAMS" && !name.StartsWith("WAITLIST");
    }
}
