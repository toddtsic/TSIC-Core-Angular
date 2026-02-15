using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

public interface ISchedulingDashboardService
{
    Task<SchedulingDashboardStatusDto> GetStatusAsync(Guid jobId, CancellationToken ct = default);
}
