namespace TSIC.Contracts.Services;

/// <summary>
/// The ONE place a store item goes back on the shelf.
///
/// <para>
/// Restocking is TWO writes, and doing only one of them is silently wrong in a way nobody notices
/// for a season:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>StoreCartBatchSkus.Restocked</c> — the inventory fact. Every availability figure in the
///     store is <c>SUM(Quantity - Restocked)</c>, so without this the unit never returns to sale.
///   </description></item>
///   <item><description>
///     a <c>StoreCartBatchSkuRestocks</c> row — the audit fact, and the only source for the
///     Restocked report.
///   </description></item>
/// </list>
///
/// <para>
/// Both halves have been missed in production. Legacy's IStoreService.LogRestock constructs the
/// log row and never calls Add() on it — so its Restocked report has been empty since the feature
/// shipped, despite units genuinely being restocked. Our own admin restock button had the mirror
/// bug: it wrote the log row but never touched Restocked, so the unit was recorded as returned and
/// still could not be sold. Neither could happen with a single writer, which is why this exists.
/// </para>
/// </summary>
public interface IStoreRestockService
{
    /// <summary>
    /// Return <paramref name="count"/> units of a purchased line to sellable inventory and record
    /// why. Refuses to restock more than were bought.
    ///
    /// <para>
    /// STAGES the changes; it does NOT save. The caller owns the SaveChanges so a restock commits
    /// in the same unit of work as the refund or void that caused it — a restock that survives a
    /// failed refund puts stock back that was never returned.
    /// </para>
    /// </summary>
    Task StageRestockAsync(
        Guid jobId, int storeCartBatchSkuId, int count, string userId, CancellationToken ct = default);
}
