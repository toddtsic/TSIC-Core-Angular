using TSIC.Contracts.Dtos.LastMonthsJobStats;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

public class LastMonthsJobStatsService : ILastMonthsJobStatsService
{
    private readonly ILastMonthsJobStatsRepository _repo;

    public LastMonthsJobStatsService(ILastMonthsJobStatsRepository repo)
    {
        _repo = repo;
    }

    public Task<List<LastMonthsJobStatRowDto>> GetLastMonthsAsync(CancellationToken cancellationToken = default)
        => _repo.GetLastMonthsAsync(cancellationToken);

    public Task<bool> UpdateCountsAsync(
        int aid,
        UpdateLastMonthsJobStatRequest request,
        string lebUserId,
        CancellationToken cancellationToken = default)
        => _repo.UpdateCountsAsync(aid, request, lebUserId, cancellationToken);
}
