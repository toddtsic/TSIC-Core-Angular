using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// Hand-drawn (Syncfusion.Pdf) renderer for the director-built PackedRoster Designer.
/// Replaces the canned Bold Reports RDLs for the Tournament Roster "Packed" family —
/// one stored proc (the full field universe) + a render config, no RDL, no Bold.
/// </summary>
public interface IPackedRosterPdfService
{
    /// <summary>
    /// Static metadata for the player-row columns the Designer can place. Drives the
    /// frontend field picker so the available pool is never hard-coded client-side.
    /// </summary>
    IReadOnlyList<PackedRosterFieldDto> GetAvailableFields();

    /// <summary>
    /// Renders the packed-roster PDF for a job from the given Designer config.
    /// </summary>
    Task<ReportExportResult> GenerateAsync(
        PackedRosterRequestDto request,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
