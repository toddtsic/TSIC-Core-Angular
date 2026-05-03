using TSIC.Contracts.Dtos.LastMonthsJobStats;

namespace TSIC.Contracts.Services;

public interface ILastMonthsJobStatsService
{
    Task<List<LastMonthsJobStatRowDto>> GetLastMonthsAsync(CancellationToken cancellationToken = default);

    Task<bool> UpdateCountsAsync(
        int aid,
        UpdateLastMonthsJobStatRequest request,
        string lebUserId,
        CancellationToken cancellationToken = default);
}
