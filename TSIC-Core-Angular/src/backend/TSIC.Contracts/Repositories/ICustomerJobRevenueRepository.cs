using TSIC.Contracts.Dtos.CustomerJobRevenue;

namespace TSIC.Contracts.Repositories;

public interface ICustomerJobRevenueRepository
{
    /// <summary>
    /// Executes the appropriate CustomerJobRevenueRollups stored procedure and reads all 6 result sets.
    /// </summary>
    /// <param name="jobId">Current job context.</param>
    /// <param name="startDate">Revenue period start.</param>
    /// <param name="endDate">Revenue period end.</param>
    /// <param name="listJobsString">Comma-delimited job name filter (empty string for all).</param>
    /// <param name="isTsicAdn">True to use TSIC ADN sproc variant; false for customer-owned ADN variant.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<JobRevenueDataDto> GetRevenueDataAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        string listJobsString, bool isTsicAdn,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a single MonthlyJobStats row (inline edit from the counts grid).
    /// </summary>
    Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default);
}
