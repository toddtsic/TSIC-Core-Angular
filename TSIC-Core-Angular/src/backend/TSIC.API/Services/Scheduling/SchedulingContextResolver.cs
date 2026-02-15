using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

public sealed class SchedulingContextResolver : ISchedulingContextResolver
{
    private readonly IJobLeagueRepository _jobLeagueRepo;
    private readonly IJobRepository _jobRepo;

    public SchedulingContextResolver(
        IJobLeagueRepository jobLeagueRepo,
        IJobRepository jobRepo)
    {
        _jobLeagueRepo = jobLeagueRepo;
        _jobRepo = jobRepo;
    }

    public async Task<(Guid LeagueId, string Season, string Year)> ResolveAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var leagueId = await _jobLeagueRepo.GetPrimaryLeagueForJobAsync(jobId, ct)
            ?? throw new InvalidOperationException($"No primary league found for job {jobId}.");

        var seasonYear = await _jobRepo.GetJobSeasonYearAsync(jobId, ct)
            ?? throw new InvalidOperationException($"No season/year found for job {jobId}.");

        return (leagueId, seasonYear.Season ?? "", seasonYear.Year ?? "");
    }
}
