namespace TSIC.Contracts.Dtos;

/// <summary>
/// Export format options for Crystal Reports.
/// Values match the legacy CRExportFormats enum.
/// </summary>
public enum ReportExportFormat
{
    Pdf = 1,
    Rtf = 2,
    Xls = 3
}

/// <summary>
/// Request payload sent to the external Crystal Reports service.
/// All fields are derived server-side from JWT claims — never from client parameters.
/// </summary>
public record CrystalReportRequest
{
    public required string RptName { get; init; }
    public required Guid JobId { get; init; }
    public required string UserId { get; init; }
    public Guid? RegId { get; init; }
    public required int ExportFormat { get; init; }
    public string? StrGids { get; init; }
}

/// <summary>
/// Result of a report export operation, containing the file bytes and metadata.
/// </summary>
public record ReportExportResult
{
    public required byte[] FileBytes { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}

/// <summary>
/// Result of a month-end close export: a single .zip containing two independent QuickBooks .iif
/// files (registration + merch) and their backing .xlsx, plus per-stack transaction (TRNS) counts
/// used to validate that consolidation dropped nothing (source total should equal consolidated total).
/// </summary>
public record ReconciliationBundleResult
{
    public required ReportExportResult Zip { get; init; }
    public required int RegSourceTrnsCount { get; init; }
    public required int RegConsolidatedTrnsCount { get; init; }
    public required int MerchSourceTrnsCount { get; init; }
    public required int MerchConsolidatedTrnsCount { get; init; }

    /// <summary>False when the close shipped without the eCheck returns file (sproc unavailable).</summary>
    public bool EcheckReturnsFileBuilt { get; init; }
    public int EcheckReturnsSourceTrnsCount { get; init; }
    public int EcheckReturnsConsolidatedTrnsCount { get; init; }
}

/// <summary>
/// Request model for schedule export with game IDs.
/// </summary>
public record ScheduleExportRequest
{
    public required string ExportFormat { get; init; }
    public required string StrListGids { get; init; }
}

/// <summary>
/// Request model for iCal schedule export.
/// </summary>
public record ScheduleICalExportRequest
{
    public required string StrListGidsIcal { get; init; }
}
