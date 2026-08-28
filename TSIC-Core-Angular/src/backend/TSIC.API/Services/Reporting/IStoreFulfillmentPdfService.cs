using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Reporting;

/// <summary>
/// The three store fulfilment PDFs (Syncfusion.Pdf) — the EF/code-gen replacement for the Crystal
/// reports <c>StoreLabels3</c>, <c>StorePerPlayerPickup</c> and <c>StorePerPlayerPivot</c>.
///
/// <para>Those three <c>.rpt</c> files never existed: the Crystal host holds 110 report files and
/// not one begins with "Store", so legacy's Labels menu was pointing at nothing long before the
/// host was switched off. These are new builds against the same two backing procs' logic, with
/// the club-rep and registration joins relaxed so walk-up sales stop disappearing.</para>
///
/// <para>One interface for all three because they share a data source and a job: get the right
/// goods into the right hands at the pickup table.</para>
/// </summary>
public interface IStoreFulfillmentPdfService
{
    /// <summary>
    /// Bag labels, one per family+player, on Avery 5163 stock (2" x 4", 10 to a Letter sheet).
    /// A player whose items overrun one label continues onto a second ("Label 1 of 2") rather
    /// than truncating.
    /// </summary>
    Task<ReportExportResult> GenerateBagLabelsAsync(
        Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-family pickup signoff sheet: every family's full order with a signature and date line,
    /// pre-filled where the batch has already been signed for.
    /// </summary>
    Task<ReportExportResult> GeneratePickupSignoffAsync(
        Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-family pivot: families down the side, SKUs across the top, landscape. Column panels
    /// repeat the family column when the SKU count exceeds one page width.
    /// </summary>
    Task<ReportExportResult> GenerateFamilyPivotAsync(
        Guid jobId, CancellationToken cancellationToken = default);
}
