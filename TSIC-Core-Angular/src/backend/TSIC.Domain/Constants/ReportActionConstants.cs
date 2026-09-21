namespace TSIC.Domain.Constants;

/// <summary>
/// Report action names that more than one place has to agree on.
///
/// An action name is data as much as code: it is the <c>Action</c> column of a
/// reporting.JobReports row, the route the client menu dispatches, and the
/// <c>ReportName</c> written to Jobs.JobReportExportHistory. A report that reads the
/// history has to spell it exactly as the endpoint wrote it, and a literal repeated in
/// two layers is a silent miss waiting to happen -- the reader would simply return no
/// rows, which reads as "nobody exported".
/// </summary>
public static class ReportActionConstants
{
    /// <summary>
    /// Authorized Rosters and Schedule Export -- the in-house replacement for the retired
    /// SportsRecruits API. Seeded as reporting.JobReports Action, served by
    /// ReportingController, and recorded per run in Jobs.JobReportExportHistory.
    /// </summary>
    public const string ThirdPartyRosterExport = "ThirdPartyRosterExport";
}
