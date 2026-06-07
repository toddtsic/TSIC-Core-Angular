using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the monthly client-invoice reports — the EF
/// replacement for the legacy Crystal "invoices2015" (itemized) and "invoices2015SummariesOnly"
/// (summary-only), both backed by <c>adn.rpt_invoice</c>. One flat EF line dataset
/// (<see cref="IReportingRepository.GetInvoiceLinesAsync"/>) is grouped Venue → Payment Category
/// and rendered as the itemized payment tables + a per-venue Accounting Summary. Runs for the most
/// recently completed month across ALL jobs (no job scoping).
/// </summary>
public interface IInvoiceReportPdfService
{
    /// <summary>Itemized: per-venue payment line tables + Accounting Summary (legacy "invoices2015").</summary>
    Task<ReportExportResult> GenerateItemizedAsync(CancellationToken cancellationToken = default);

    /// <summary>Summary-only: per-venue Accounting Summary, one page each (legacy "...SummariesOnly").</summary>
    Task<ReportExportResult> GenerateSummaryOnlyAsync(CancellationToken cancellationToken = default);
}
