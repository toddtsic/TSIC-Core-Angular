using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

/// <summary>
/// Store item images, ported from legacy StoreImagesController + IStoreService.GetJobItemsPictures.
///
/// <para>
/// The FILESYSTEM is the source of truth, as in legacy: whatever files exist under the store
/// images folder matching <c>{storeId}-{storeItemId}-{instance}.jpg</c> ARE that item's images.
/// Legacy has no image table at all — it enumerates the directory on every read.
/// </para>
///
/// <para>
/// <c>stores.StoreItemImage</c> is NOT a second source of truth; it is a read index so the
/// shopper-facing catalog can project image URLs inside its existing query instead of hitting the
/// disk per item. Every method here re-syncs that index from disk for the items it touches, which
/// is also what repairs the drift already present in the data (20 index rows against 34 files —
/// the index recorded only each item's first instance).
/// </para>
/// </summary>
public interface IStoreImageService
{
    /// <summary>
    /// Every image in the job's store, one row per file, ordered by item then instance. Items with
    /// no file on disk yield a single placeholder row (legacy GetJobItemsPictures). Re-syncs the
    /// StoreItemImage index for the whole store.
    /// </summary>
    Task<List<StoreItemImageDto>> GetStoreImagesAsync(
        Guid jobId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Images for one item, ordered by instance. Empty when the item has no file — no placeholder
    /// row, because the caller asked about one known item.
    /// </summary>
    Task<List<StoreItemImageDto>> GetItemImagesAsync(
        Guid jobId, int storeItemId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Add an image. Refused past <see cref="MaxImagesPerItem"/>. The new file takes the next
    /// instance number (highest existing + 1), matching legacy CalculateNextInstance.
    /// </summary>
    Task<StoreItemImageDto> AddItemImageAsync(
        Guid jobId, int storeItemId, Stream fileStream, string originalFileName,
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Replace the bytes of an existing image, keeping its instance number and its position in
    /// the item's image order (legacy HandleNormalUpdate).
    /// </summary>
    Task<StoreItemImageDto> ReplaceItemImageAsync(
        Guid jobId, int storeItemId, int instance, Stream fileStream, string originalFileName,
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Delete an image and renumber what remains so instances stay contiguous from 1, matching
    /// legacy RenumberImagesAfterDeletion. Renumbering is two-phase (to temp names, then to final)
    /// so a shift never collides with a name still in use.
    /// </summary>
    Task DeleteItemImageAsync(
        Guid jobId, int storeItemId, int instance, string userId, CancellationToken ct = default);

    /// <summary>Legacy MAX_IMAGES_PER_ITEM.</summary>
    const int MaxImagesPerItem = 10;
}
