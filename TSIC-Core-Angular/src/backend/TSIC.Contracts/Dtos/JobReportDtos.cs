namespace TSIC.Contracts.Dtos;

/// <summary>
/// One row from <c>reporting.JobReports</c> — the per-(Job, Role) reports library.
/// Returned by <c>GET /api/reporting/catalogue</c> filtered to the caller's roles
/// + active rows. Server has already applied the (jobId, role IN callerRoles, Active=1)
/// gate, so every row in the response is one the calling user is entitled to run.
///
/// Client uses Controller + Action verbatim to drive the export request — Action is
/// the legacy menu URL (e.g. <c>ExportStoredProcedureResults?spName=[reporting].[Foo]&amp;bUseJobId=true</c>
/// for stored-proc reports, or a bare endpoint name for Crystal reports / Home/ShowJobInvoices).
/// Kind ('StoredProcedure' | 'CrystalReport') drives which export path the client picks.
/// </summary>
public record JobReportEntryDto
{
    public required Guid JobReportId { get; init; }
    public required string Title { get; init; }
    public string? IconName { get; init; }
    public required string Controller { get; init; }
    public required string Action { get; init; }

    /// <summary>'StoredProcedure' or 'CrystalReport' — derived at populate time from Action prefix.</summary>
    public required string Kind { get; init; }

    /// <summary>Legacy L1 group label (e.g. 'Reports', 'Recruiting'). Drives tab grouping. Null = uncategorized.</summary>
    public string? GroupLabel { get; init; }

    public required int SortOrder { get; init; }
    public required bool Active { get; init; }
}
