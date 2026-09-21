namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// Third-Party Roster Exports: every run of the Authorized Rosters and Schedule Export
/// against the scoped live events, per event, with the dated log behind the counts.
///
/// Source is Jobs.JobReportExportHistory in TSICV5, NOT the usage log. The endpoint has
/// written a history row on every successful run since the report existed, where the log
/// only begins 2026-09-04 and admits a row only when the request carried a client tag. On
/// this box the history holds 8 runs from 2026-08-11 and the log holds 1: for a record of
/// who took minors' data out of an event, the lossy source is the wrong one.
///
/// Failures are not here. A refused run exported nothing (Todd, 2026-09-20: count successes
/// only) and the history is only written after the file is produced.
/// </summary>
public record ThirdPartyExportsDto
{
    public required int WindowDays { get; init; }

    /// <summary>Live events the scope covers -- including those that exported nothing.</summary>
    public required int JobCount { get; init; }

    /// <summary>One row per event that exported at least once. An event with no runs is not a bar.</summary>
    public required List<ThirdPartyExportRowDto> Rows { get; init; }

    /// <summary>
    /// The months that saw an export, "yyyy/MM", oldest first -- the column order, and the
    /// bars inside each event's group.
    ///
    /// Months with no export anywhere are left out rather than carried as empty columns: a
    /// year-long window would otherwise be mostly blank, and the report already leaves out
    /// events that exported nothing. The axis is therefore the months that happened, not a
    /// regular calendar.
    /// </summary>
    public required List<string> Months { get; init; }

    /// <summary>The runs themselves, newest first: who exported which event, and when.</summary>
    public required List<ThirdPartyExportLogEntryDto> Log { get; init; }

    public required int TotalExports { get; init; }

    public required int EventsExported { get; init; }

    /// <summary>Distinct people who ran it in the window.</summary>
    public required int Exporters { get; init; }

    /// <summary>
    /// True when the window held more runs than <see cref="Log"/> carries. The counts are
    /// still whole -- only the listing is capped -- and the page must say so rather than let
    /// the last row read as the oldest run.
    /// </summary>
    public required bool LogTruncated { get; init; }
}

/// <summary>One event's export count: a bar on the chart, a row in the table.</summary>
public record ThirdPartyExportRowDto
{
    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    /// <summary>Every run of this event in the window -- the row's Total, and the sum of its months.</summary>
    public required int Exports { get; init; }

    /// <summary>Most recent run of this event in the window.</summary>
    public required DateTime LastExport { get; init; }

    /// <summary>
    /// This event's runs split by month, oldest first. Only months this event exported in;
    /// a month it sat out is absent rather than zero, and the page renders the gap.
    /// </summary>
    public required List<ThirdPartyExportMonthDto> MonthCounts { get; init; }
}

/// <summary>One month of one event's exports.</summary>
public record ThirdPartyExportMonthDto
{
    /// <summary>"yyyy/MM". Sorts chronologically as text, which is why it is stored that way.</summary>
    public required string Month { get; init; }

    public required int Exports { get; init; }
}

/// <summary>
/// One run. Named from the registration that ran it, so a vendor alias with no first or
/// last name still reads as somebody: the login stands in for the name.
/// </summary>
public record ThirdPartyExportLogEntryDto
{
    public required Guid JobId { get; init; }

    public required string JobName { get; init; }

    /// <summary>"First Last", or the login when the account carries no name.</summary>
    public required string ExporterName { get; init; }

    public required string ExporterLogin { get; init; }

    public required string RoleName { get; init; }

    /// <summary>
    /// True for the ApiAuthorized vendor login -- an outside agency. False means one of the
    /// client's own admins ran the same export, which is not a third-party release and is
    /// shown as itself rather than hidden.
    /// </summary>
    public required bool IsThirdParty { get; init; }

    public required DateTime ExportedAt { get; init; }
}
