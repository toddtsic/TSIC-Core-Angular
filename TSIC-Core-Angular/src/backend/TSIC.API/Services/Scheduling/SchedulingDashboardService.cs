using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

public sealed class SchedulingDashboardService : ISchedulingDashboardService
{
    private readonly IFieldRepository _fieldRepo;
    private readonly IPairingsRepository _pairingsRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly ITimeslotRepository _timeslotRepo;
    private readonly ISchedulingContextResolver _contextResolver;

    public SchedulingDashboardService(
        IFieldRepository fieldRepo,
        IPairingsRepository pairingsRepo,
        IScheduleRepository scheduleRepo,
        ITeamRepository teamRepo,
        ITimeslotRepository timeslotRepo,
        ISchedulingContextResolver contextResolver)
    {
        _fieldRepo = fieldRepo;
        _pairingsRepo = pairingsRepo;
        _scheduleRepo = scheduleRepo;
        _teamRepo = teamRepo;
        _timeslotRepo = timeslotRepo;
        _contextResolver = contextResolver;
    }

    public async Task<SchedulingDashboardStatusDto> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, year) = await _contextResolver.ResolveAsync(jobId, ct);

        // Sequential queries — repositories share a single scoped DbContext
        var fields = await _fieldRepo.GetLeagueSeasonFieldsAsync(leagueId, season, ct);
        var agegroups = await _pairingsRepo.GetAgegroupsWithDivisionsAsync(leagueId, season, ct);
        var teamCountsByAg = await _teamRepo.GetRegistrationCountsByAgeGroupAsync(jobId, ct);
        var teamCountsByDiv = await _teamRepo.GetTeamCountsByDivisionAsync(jobId, ct);
        var poolSizesWithPairings = await _pairingsRepo.GetDistinctPoolSizesWithPairingsAsync(leagueId, season, ct);
        var agsWithDates = await _timeslotRepo.GetAgegroupIdsWithDatesAsync(season, year, ct);
        var agsWithFieldTimeslots = await _timeslotRepo.GetAgegroupIdsWithFieldTimeslotsAsync(season, year, ct);
        var gamesByDiv = await _scheduleRepo.GetRoundRobinGameCountsByDivisionAsync(jobId, ct);

        // Filter out utility agegroups
        var filteredAgegroups = agegroups
            .Where(ag =>
            {
                var name = (ag.AgegroupName ?? "").ToUpper();
                return name != "DROPPED TEAMS" && !name.StartsWith("WAITLIST");
            })
            .ToList();

        var allDivisions = filteredAgegroups
            .SelectMany(ag => ag.Divisions ?? [])
            .Where(d => (d.DivName ?? "").ToUpper() != "UNASSIGNED")
            .ToList();

        var totalAgegroups = filteredAgegroups.Count;
        var totalDivisions = allDivisions.Count;

        // Card 1 — LADT: DivisionsAreThemed = every agegroup has a color
        var divisionsAreThemed = filteredAgegroups.Count > 0
            && filteredAgegroups.All(ag => !string.IsNullOrEmpty(ag.Color));

        // Card 2 — Pool Assignment: agegroups where all teams are in real divisions
        var agegroupsPoolComplete = 0;
        foreach (var ag in filteredAgegroups)
        {
            var totalForAg = teamCountsByAg.TryGetValue(ag.AgegroupId, out var cnt) ? cnt : 0;
            if (totalForAg == 0) continue;
            var divIds = (ag.Divisions ?? [])
                .Where(d => (d.DivName ?? "").ToUpper() != "UNASSIGNED")
                .Select(d => d.DivId)
                .ToList();
            var assignedForAg = divIds.Sum(did => teamCountsByDiv.TryGetValue(did, out var c) ? c : 0);
            if (assignedForAg >= totalForAg) agegroupsPoolComplete++;
        }

        // Card 4 — Pairings: distinct pool sizes across real divisions
        var distinctPoolSizes = new HashSet<int>();
        foreach (var div in allDivisions)
        {
            if (teamCountsByDiv.TryGetValue(div.DivId, out var tc) && tc >= 2)
                distinctPoolSizes.Add(tc);
        }

        // Card 5 — Timeslots: agegroups that have both dates AND field-timeslots
        var agegroupsReady = filteredAgegroups.Count(ag =>
            agsWithDates.Contains(ag.AgegroupId) && agsWithFieldTimeslots.Contains(ag.AgegroupId));

        // Card 6 — Schedule: agegroups with at least one division that has scheduled games
        var divsWithGames = new HashSet<Guid>(gamesByDiv.Where(kv => kv.Value > 0).Select(kv => kv.Key));
        var agegroupsScheduled = filteredAgegroups.Count(ag =>
            (ag.Divisions ?? []).Any(d => divsWithGames.Contains(d.DivId)));

        return new SchedulingDashboardStatusDto
        {
            TotalAgegroups = totalAgegroups,
            TotalDivisions = totalDivisions,
            DivisionsAreThemed = divisionsAreThemed,
            AgegroupsPoolComplete = agegroupsPoolComplete,
            FieldCount = fields.Count,
            PoolSizesWithPairings = poolSizesWithPairings.Count,
            TotalDistinctPoolSizes = distinctPoolSizes.Count,
            AgegroupsReady = agegroupsReady,
            AgegroupsScheduled = agegroupsScheduled
        };
    }
}
