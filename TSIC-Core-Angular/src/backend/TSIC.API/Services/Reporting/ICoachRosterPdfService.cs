using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// The legacy "Rosters for Coaches (pdf)" layout — the coach's field copy, distinct from the
/// "Coaches Eyes Only" club roster (<see cref="IClubRosterPdfService"/>).
///
/// These were two different Crystal <c>.rpt</c> files, not one layout behind a flag. This one
/// leads with the uniform number, gives School/HomeTown its own column, carries no DOB, no email,
/// no medical note and <b>no financial column</b>, and starts each team on a fresh page.
/// </summary>
public interface ICoachRosterPdfService
{
    /// <param name="jobId">The job whose active rostered players are rendered.</param>
    Task<ReportExportResult> GenerateAsync(Guid jobId, CancellationToken cancellationToken = default);
}
