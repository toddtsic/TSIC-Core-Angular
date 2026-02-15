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
    private readonly IJobLeagueRepository _jobLeagueRepo;
    private readonly IJobRepository _jobRepo;

    public SchedulingDashboardService(
        IFieldRepository fieldRepo,
        IPairingsRepository pairingsRepo,
        IScheduleRepository scheduleRepo,
        ITeamRepository teamRepo,
        IJobLeagueRepository jobLeagueRepo,
        IJobRepository jobRepo)
    {
        _fieldRepo = fieldRepo;
        _pairingsRepo = pairingsRepo;
        _scheduleRepo = scheduleRepo;
        _teamRepo = teamRepo;
        _jobLeagueRepo = jobLeagueRepo;
        _jobRepo = jobRepo;
    }

    public async Task<SchedulingDashboardStatusDto> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        var (leagueId, season, _) = await ResolveLeagueSeasonYearAsync(jobId, ct);

        // Sequential queries — repositories share a single scoped DbContext
        var fields = await _fieldRepo.GetLeagueSeasonFieldsAsync(leagueId, season, ct);
        var agegroups = await _pairingsRepo.GetAgegroupsWithDivisionsAsync(leagueId, season, ct);
        var (gameCount, divsScheduled) = await _scheduleRepo.GetSchedulingDashboardStatsAsync(jobId, ct);
        var teamCountsByAg = await _teamRepo.GetRegistrationCountsByAgeGroupAsync(jobId, ct);
        var teamCountsByDiv = await _teamRepo.GetTeamCountsByDivisionAsync(jobId, ct);

        // Derive counts from hierarchy
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

        // Teams assigned = teams in real (non-UNASSIGNED) divisions
        var realDivIds = new HashSet<Guid>(allDivisions.Select(d => d.DivId));
        var teamsAssigned = teamCountsByDiv
            .Where(kv => realDivIds.Contains(kv.Key))
            .Sum(kv => kv.Value);
        var totalTeams = teamCountsByAg.Values.Sum();
        var teamsUnassigned = Math.Max(0, totalTeams - teamsAssigned);

        // Divisions with pairings ≈ divisions with ≥2 teams (pairings require ≥2 teams)
        var divsWithPairings = teamCountsByDiv
            .Count(kv => realDivIds.Contains(kv.Key) && kv.Value >= 2);

        return new SchedulingDashboardStatusDto
        {
            FieldCount = fields.Count,
            DivisionsWithPairings = divsWithPairings,
            TotalPairingCount = 0,
            AgegroupsWithTimeslots = totalAgegroups,
            TimeslotDateCount = 0,
            ScheduledGameCount = gameCount,
            DivisionsScheduled = divsScheduled,
            TotalDivisions = totalDivisions,
            TotalAgegroups = totalAgegroups,
            TeamsAssigned = teamsAssigned,
            TeamsUnassigned = teamsUnassigned
        };
    }

    private async Task<(Guid leagueId, string season, string year)> ResolveLeagueSeasonYearAsync(
        Guid jobId, CancellationToken ct)
    {
        var leagueId = await _jobLeagueRepo.GetPrimaryLeagueForJobAsync(jobId, ct)
            ?? throw new InvalidOperationException($"No primary league found for job {jobId}.");

        var seasonYear = await _jobRepo.GetJobSeasonYearAsync(jobId, ct)
            ?? throw new InvalidOperationException($"No season/year found for job {jobId}.");

        return (leagueId, seasonYear.Season ?? "", seasonYear.Year ?? "");
    }
}
