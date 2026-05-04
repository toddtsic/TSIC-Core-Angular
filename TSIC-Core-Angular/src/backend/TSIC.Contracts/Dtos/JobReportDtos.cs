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

/// <summary>
/// Editor view of a row from reporting.JobReports — extends <see cref="JobReportEntryDto"/>
/// with audit fields for the SuperUser editor's display + change tracking.
/// </summary>
public record JobReportEditorRowDto
{
    public required Guid JobReportId { get; init; }
    public required string Title { get; init; }
    public string? IconName { get; init; }
    public required string Controller { get; init; }
    public required string Action { get; init; }
    public required string Kind { get; init; }
    public string? GroupLabel { get; init; }
    public required int SortOrder { get; init; }
    public required bool Active { get; init; }
    public required DateTime Modified { get; init; }
    public string? LebUserId { get; init; }
}

/// <summary>
/// Role-picker dropdown row — one per role that has any rows in reporting.JobReports
/// for the current job. RowCount drives the "(N entries)" badge in the picker UI.
/// </summary>
public record JobReportEditorRoleDto
{
    public required string RoleId { get; init; }
    public required string RoleName { get; init; }
    public required int RowCount { get; init; }
}

/// <summary>
/// Editor update payload. Title / IconName / GroupLabel / SortOrder / Active are
/// SU-editable. Controller / Action / Kind / RoleId / JobId are immutable — they
/// either bind the row to the actual report or scope it to (Job, Role).
/// </summary>
public record JobReportEditorUpdateDto
{
    public required string Title { get; init; }
    public string? IconName { get; init; }
    public string? GroupLabel { get; init; }
    public required int SortOrder { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// Editor create payload. JobId is server-derived from JWT (never trusted from client).
/// RoleId is the currently-selected picker role on the editor — server validates the
/// (JobId, RoleId, Controller, Action, GroupLabel) tuple is unique before insert.
/// </summary>
public record JobReportEditorCreateDto
{
    public required string RoleId { get; init; }
    public required string Title { get; init; }
    public string? IconName { get; init; }
    public required string Controller { get; init; }
    public required string Action { get; init; }
    public required string Kind { get; init; }
    public string? GroupLabel { get; init; }
    public required int SortOrder { get; init; }
    public required bool Active { get; init; }
}
