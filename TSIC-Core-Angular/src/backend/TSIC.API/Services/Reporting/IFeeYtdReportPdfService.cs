using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the TSIC-fee year-to-date comparison reports — the EF
/// replacement for the legacy Crystal "tsicTSICFeesYTD" (by customer + job) and
/// "tsicTSICFeesYTDByCustomer" (customer rollup), both backed by <c>adn.tsicFeesYTDAndLastYear</c>.
/// One flat EF row set (<see cref="IReportingRepository.GetFeeYtdRowsAsync"/>) is rolled up to a
/// this-year-YTD vs last-year-YTD (same months 1..lastMonth) comparison, grouped by customer (and
/// job). Runs across ALL jobs (no job scoping); the period is the most recently completed month.
/// </summary>
public interface IFeeYtdReportPdfService
{
    /// <summary>By customer → job breakout, with per-customer and grand totals (legacy "tsicTSICFeesYTD").</summary>
    Task<ReportExportResult> GenerateByCustomerAndJobAsync(CancellationToken cancellationToken = default);

    /// <summary>By customer rollup, with a grand total (legacy "tsicTSICFeesYTDByCustomer").</summary>
    Task<ReportExportResult> GenerateByCustomerAsync(CancellationToken cancellationToken = default);
}
