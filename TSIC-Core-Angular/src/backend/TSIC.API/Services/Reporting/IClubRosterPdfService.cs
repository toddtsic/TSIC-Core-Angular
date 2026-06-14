using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn "Coaches Eyes Only" club roster PDF (Syncfusion.Pdf) — the EF replacement for the
/// legacy Crystal family. One fixed team-grouped layout drives all four legacy reports; the caller
/// picks the scope (single job vs every job of the job's customer) and whether the medical note
/// is included.
/// </summary>
public interface IClubRosterPdfService
{
    /// <param name="jobId">The requesting job. When <paramref name="allCustomerJobs"/> is true this
    /// only identifies the customer; otherwise it is the single job rendered.</param>
    /// <param name="allCustomerJobs">True = every job of this job's customer (the cross-job
    /// <c>Club_AllJobs_Rosters_NoMedical</c>); false = just this job.</param>
    /// <param name="includeMedical">True = show the medical note line (legacy <c>Job_Club_Rosters</c>);
    /// false = withhold it (the "No Medical" variants).</param>
    Task<ReportExportResult> GenerateAsync(
        Guid jobId,
        bool allCustomerJobs,
        bool includeMedical,
        CancellationToken cancellationToken = default);
}
