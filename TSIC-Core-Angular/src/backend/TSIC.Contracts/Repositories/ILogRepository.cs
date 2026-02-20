using TSIC.Contracts.Dtos.Logs;

namespace TSIC.Contracts.Repositories;

public interface ILogRepository
{
    Task<(List<LogEntryDto> Items, int TotalCount)> QueryAsync(LogQueryParams query, CancellationToken ct = default);
    Task<LogStatsDto> GetStatsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
    Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
