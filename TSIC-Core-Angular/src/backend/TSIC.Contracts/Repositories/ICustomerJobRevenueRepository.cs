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
    /// Available job names for the caller's customer group (numeric year ≥ 2022).
    /// Cheap scope-picker query — replaces running the full report just to fill the dropdown.
    /// </summary>
    Task<List<string>> GetAvailableJobNamesAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// EF port of the CustomerJobRevenueRollups sproc pair (sets #1–#3), scoped at the source.
    /// Scope: non-empty <paramref name="jobNames"/> = those jobs' complete history (dates ignored);
    /// otherwise the mandatory date range bounds all jobs in the customer group.
    /// </summary>
    /// <param name="isTsicAdn">True = settlement-based amounts from adn.vTxs with CC/E-Check fee rollups; false = Registration_Accounting amounts, no fee rollups.</param>
    Task<RevenueRollupResponseDto> GetRollupAsync(
        Guid jobId, bool isTsicAdn,
        DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default);

    /// <summary>
    /// EF port of the sproc detail sets (#4 CC / #5 Check / #7 E-Check) for one payment family,
    /// fetched lazily when its tab opens. Same scope semantics as <see cref="GetRollupAsync"/>.
    /// </summary>
    /// <param name="method">"cc" | "check" | "echeck".</param>
    Task<List<JobPaymentRecordDto>> GetPaymentDetailsAsync(
        Guid jobId, bool isTsicAdn, string method,
        DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a single MonthlyJobStats row (inline edit from the counts grid).
    /// </summary>
    Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default);
}
