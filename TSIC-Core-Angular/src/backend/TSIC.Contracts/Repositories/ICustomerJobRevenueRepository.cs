using TSIC.Contracts.Dtos.CustomerJobRevenue;

namespace TSIC.Contracts.Repositories;

public interface ICustomerJobRevenueRepository
{
    /// <summary>
    /// Executes the legacy CustomerJobRevenueRollups sproc (variant per isTsicAdn) and reads
    /// all 7 result sets. Baseline source for the SuperUser live QA comparison ONLY.
    /// </summary>
    Task<JobRevenueDataDto> GetLegacySprocDataAsync(
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
    /// Team Billing tab: every ACTIVE team in scope with its lifetime billed/collected/owed,
    /// bucketed by <c>teams.createdate</c>. Team-driven, not payment-driven — a team that never
    /// paid still returns a row (with zeros), which is the point of the report.
    /// </summary>
    /// <remarks>
    /// Scope semantics match <see cref="GetRollupAsync"/>: non-empty <paramref name="jobNames"/>
    /// = those jobs complete, dates ignored; otherwise the date range bounds <c>createdate</c>.
    /// </remarks>
    Task<List<TeamBillingRecordDto>> GetTeamBillingAsync(
        Guid jobId, DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a single MonthlyJobStats row (inline edit from the counts grid).
    /// </summary>
    Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default);
}
