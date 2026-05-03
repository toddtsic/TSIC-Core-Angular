namespace TSIC.Contracts.Dtos;

/// <summary>
/// Visible (active + visibility-filtered) Type 2 report catalogue entry,
/// returned by GET /api/reporting/catalogue.
///
/// Server has already applied <see cref="NavItemVisibilityRules"/> filtering
/// against the current job context, so every row in the response is one the
/// current admin can run. VisibilityRules is returned for display/debug
/// purposes (e.g. "gated to Tournament Scheduling only") — the client does
/// NOT re-evaluate it for gating.
/// </summary>
public record ReportCatalogueEntryDto
{
    public required Guid ReportId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? IconName { get; init; }

    /// <summary>Schema-qualified proc name (e.g. <c>reporting.RefAssignmentQA</c>).</summary>
    public required string StoredProcName { get; init; }

    /// <summary>JSON binding of runtime values to SP input params. Null = server defaults.</summary>
    public string? ParametersJson { get; init; }

    /// <summary>Same JSON shape as <c>NavItemVisibilityRules</c>. Informational only.</summary>
    public string? VisibilityRules { get; init; }

    /// <summary>Presentation-layer grouping for the reports library UI. Allowed values
    /// are enforced in the frontend ReportCategory union. Null = uncategorized.</summary>
    public string? CategoryCode { get; init; }

    public required int SortOrder { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// Write payload for POST / PUT against reporting.ReportCatalogue.
/// SuperUser-only — see ReportingController catalogue endpoints.
/// ReportId is path-bound on PUT, ignored on POST.
/// </summary>
public record ReportCatalogueWriteDto
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? IconName { get; init; }
    public required string StoredProcName { get; init; }
    public string? ParametersJson { get; init; }
    public string? VisibilityRules { get; init; }
    public string? CategoryCode { get; init; }
    public required int SortOrder { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// Result of a stored-procedure-existence check. SuperUser-only endpoint.
/// </summary>
public record VerifyStoredProcedureDto
{
    public required string StoredProcName { get; init; }
    public required bool Exists { get; init; }
}
