using TSIC.Contracts.Dtos.CustomerJobRevenue;

namespace TSIC.API.Services.Admin;

public interface ICustomerJobRevenueService
{
    Task<JobRevenueDataDto> GetRevenueDataAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        List<string> jobNames, CancellationToken ct = default);

    Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default);
}
