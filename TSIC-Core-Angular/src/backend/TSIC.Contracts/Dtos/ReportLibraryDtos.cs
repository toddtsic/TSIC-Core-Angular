namespace TSIC.Contracts.Dtos;

/// <summary>
/// One row of <c>reporting.ReportLibrary</c> as seen from a (job, role) shelf: the report
/// itself plus whether that shelf already holds it. Served by <c>GET /api/reporting/library</c>,
/// which has already applied the visibility gates (MinRoleId rank, owner customer, job-type
/// applicability) — every row returned is one the caller MAY add. The add endpoint re-checks
/// the same gates server-side; this DTO is a view, never the authority.
/// </summary>
public record ReportLibraryEntryDto
{
    public required Guid ReportLibraryId { get; init; }
    public required string ReportKey { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Tags { get; init; }

    /// <summary>One of the seven Reports Library category codes; the shelf's GroupLabel on add.</summary>
    public required string CategoryCode { get; init; }
    public string? IconName { get; init; }

    /// <summary>StoredProcedure | CrystalReport | BoldReport | SpaComponent.</summary>
    public required string Kind { get; init; }
    public required string Controller { get; init; }

    /// <summary>Canonical action string written to a new shelf row.</summary>
    public required string Action { get; init; }

    /// <summary>JobOnly | CrossJob | CrossWebsite — a fact about the query, never a gate.</summary>
    public required string Scope { get; init; }

    /// <summary>Display name of the minimum role that may hold the report (Director and above, ...).</summary>
    public required string MinRoleName { get; init; }

    /// <summary>The caller's shelf row for this report, when one exists.</summary>
    public Guid? ShelfJobReportId { get; init; }

    public bool OnShelf => ShelfJobReportId.HasValue;
}

/// <summary>The two job facts the library gates read: owner customer and job type.</summary>
public record ShelfContextDto
{
    public required Guid CustomerId { get; init; }
    public required int JobTypeId { get; init; }
}

/// <summary>Outcome of an add-to-shelf attempt; the controller maps each to a status code.</summary>
public enum ShelfAddOutcome
{
    Added,
    LibraryEntryNotFound,
    Forbidden,
    AlreadyOnShelf,
}
