using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TSIC.Contracts.Configuration;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Infrastructure.Services;

/// <summary>
/// Store item images. Port of legacy StoreImagesController + IStoreService.GetJobItemsPictures.
/// See <see cref="IStoreImageService"/> for the filesystem-is-truth contract.
/// </summary>
public class StoreImageService : IStoreImageService
{
    private readonly IStoreRepository _storeRepo;
    private readonly IStoreItemRepository _itemRepo;
    private readonly string _imagesPath;
    private readonly string _staticsBaseUrl;

    /// <summary>
    /// URL segment on statics.teamsportsinfo.com. Legacy served these from
    /// wwwroot/images/store-sku-images; on the new stack statics is its own IIS site whose root
    /// already holds Store-Sku-Images, which is where every existing file lives.
    /// </summary>
    private const string UrlFolder = "Store-Sku-Images";

    /// <summary>Legacy MISSING_IMAGE_FILE — the stand-in for an item with no photo.</summary>
    private const string MissingImageFile = "missing-image.jpg";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    /// <summary>
    /// Product photos render at card size in the catalog and at most full-width in the carousel.
    /// Anything wider is a phone-camera original and only costs the shopper bandwidth.
    /// </summary>
    private const int MaxWidth = 1200;

    private const int JpegQuality = 85;

    public StoreImageService(
        IStoreRepository storeRepo,
        IStoreItemRepository itemRepo,
        IOptions<FileStorageOptions> fileStorage,
        IOptions<TsicSettings> tsicSettings)
    {
        _storeRepo = storeRepo;
        _itemRepo = itemRepo;
        _imagesPath = fileStorage.Value.StoreImagesPath;
        _staticsBaseUrl = tsicSettings.Value.StaticsBaseUrl.TrimEnd('/');
    }

    // ═══════════════════════════════════════════
    //  READ
    // ═══════════════════════════════════════════

    public async Task<List<StoreItemImageDto>> GetStoreImagesAsync(
        Guid jobId, string userId, CancellationToken ct = default)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);
        var items = await _itemRepo.GetItemKeysForStoreAsync(storeId, ct);

        var rows = new List<StoreItemImageDto>();
        foreach (var item in items)
        {
            var instances = ReadInstances(storeId, item.StoreItemId);
            if (instances.Count == 0)
            {
                // LEGACY: an item with no file still gets a row, carrying the placeholder. That is
                // the director's "which products have no photo" readout — do not filter it out.
                rows.Add(PlaceholderRow(item));
                continue;
            }

            rows.AddRange(instances.Select(i => ToDto(storeId, item, i)));
        }

        await SyncIndexAsync(storeId, items, userId, ct);
        return rows;
    }

    public async Task<List<StoreItemImageDto>> GetItemImagesAsync(
        Guid jobId, int storeItemId, string userId, CancellationToken ct = default)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);
        var item = await AssertItemInStoreAsync(storeId, storeItemId, ct);

        var rows = ReadInstances(storeId, storeItemId)
            .Select(i => ToDto(storeId, item, i))
            .ToList();

        await SyncIndexAsync(storeId, [item], userId, ct);
        return rows;
    }

    // ═══════════════════════════════════════════
    //  WRITE
    // ═══════════════════════════════════════════

    public async Task<StoreItemImageDto> AddItemImageAsync(
        Guid jobId, int storeItemId, Stream fileStream, string originalFileName,
        string userId, CancellationToken ct = default)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);
        var item = await AssertItemInStoreAsync(storeId, storeItemId, ct);

        var existing = ReadInstances(storeId, storeItemId);
        if (existing.Count >= IStoreImageService.MaxImagesPerItem)
            throw new InvalidOperationException(
                $"Maximum of {IStoreImageService.MaxImagesPerItem} images per item exceeded.");

        // LEGACY CalculateNextInstance: highest existing + 1, never a gap-filler. The renumber on
        // delete keeps instances contiguous, so in practice this is always Count + 1.
        var nextInstance = existing.Count == 0 ? 1 : existing.Max() + 1;
        var path = FilePath(storeId, storeItemId, nextInstance);

        if (File.Exists(path))
            throw new InvalidOperationException(
                $"File {Path.GetFileName(path)} already exists. Please refresh and try again.");

        await WriteJpegAsync(fileStream, originalFileName, path, ct);
        await SyncIndexAsync(storeId, [item], userId, ct);

        return ToDto(storeId, item, nextInstance);
    }

    public async Task<StoreItemImageDto> ReplaceItemImageAsync(
        Guid jobId, int storeItemId, int instance, Stream fileStream, string originalFileName,
        string userId, CancellationToken ct = default)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);
        var item = await AssertItemInStoreAsync(storeId, storeItemId, ct);

        var path = FilePath(storeId, storeItemId, instance);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Image {instance} does not exist for this item.");

        await WriteJpegAsync(fileStream, originalFileName, path, ct);
        await SyncIndexAsync(storeId, [item], userId, ct);

        return ToDto(storeId, item, instance);
    }

    public async Task DeleteItemImageAsync(
        Guid jobId, int storeItemId, int instance, string userId, CancellationToken ct = default)
    {
        var storeId = await ResolveStoreIdAsync(jobId, ct);
        var item = await AssertItemInStoreAsync(storeId, storeItemId, ct);

        var path = FilePath(storeId, storeItemId, instance);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Image {instance} does not exist for this item.");

        File.Delete(path);
        RenumberAfterDeletion(storeId, storeItemId);
        await SyncIndexAsync(storeId, [item], userId, ct);
    }

    // ═══════════════════════════════════════════
    //  FILESYSTEM
    // ═══════════════════════════════════════════

    /// <summary>
    /// Instance numbers on disk for one item, ascending. This is the authoritative image list —
    /// legacy enumerates the directory on every read and so do we.
    /// </summary>
    private List<int> ReadInstances(int storeId, int storeItemId)
    {
        if (!Directory.Exists(_imagesPath)) return [];

        // The glob narrows the enumeration; the anchored digits-only regex is what actually
        // decides membership. It is load-bearing: the glob also matches the {store}-{item}-temp{n}
        // files that exist mid-renumber, and those must never be read back as instances.
        var regex = new Regex(
            $@"^{storeId}-{storeItemId}-(\d+)\.jpg$", RegexOptions.IgnoreCase);

        return Directory.EnumerateFiles(_imagesPath, $"{storeId}-{storeItemId}-*.jpg")
            .Select(f => regex.Match(Path.GetFileName(f)))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Where(i => i > 0)
            .OrderBy(i => i)
            .ToList();
    }

    private static string BuildFileName(int storeId, int storeItemId, int instance) =>
        $"{storeId}-{storeItemId}-{instance}.jpg";

    private string FilePath(int storeId, int storeItemId, int instance) =>
        Path.Combine(_imagesPath, BuildFileName(storeId, storeItemId, instance));

    /// <summary>
    /// LEGACY RenumberImagesAfterDeletion: close the gap so instances stay contiguous from 1.
    /// Two-phase (every mover goes to a temp name first) because shifting 3 to 2 while 2 still
    /// exists would otherwise collide.
    /// </summary>
    private void RenumberAfterDeletion(int storeId, int storeItemId)
    {
        var remaining = ReadInstances(storeId, storeItemId);
        if (remaining.Count == 0) return;

        var moves = new List<(string TempPath, string FinalPath)>();

        for (var i = 0; i < remaining.Count; i++)
        {
            var target = i + 1;
            if (remaining[i] == target) continue;

            var tempPath = Path.Combine(_imagesPath, $"{storeId}-{storeItemId}-temp{i}.jpg");
            File.Move(FilePath(storeId, storeItemId, remaining[i]), tempPath);
            moves.Add((tempPath, FilePath(storeId, storeItemId, target)));
        }

        foreach (var (tempPath, finalPath) in moves)
        {
            File.Move(tempPath, finalPath);
        }
    }

    /// <summary>
    /// Validate, downscale, and write as JPEG. Legacy took raw base64 JPEG bytes from the browser
    /// and wrote them untouched; we accept a real upload of any common format and normalize, so
    /// the .jpg filename convention stays honest about the file's contents.
    /// </summary>
    private static async Task WriteJpegAsync(
        Stream fileStream, string originalFileName, string destinationPath, CancellationToken ct)
    {
        var ext = Path.GetExtension(originalFileName)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new InvalidOperationException(
                $"File type '{ext}' is not allowed. Allowed: JPG, PNG, WebP.");

        if (fileStream.CanSeek && fileStream.Length > MaxFileSize)
            throw new InvalidOperationException(
                $"File exceeds the {MaxFileSize / (1024 * 1024)}MB limit.");

        var directory = Path.GetDirectoryName(destinationPath)!;
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        using var image = await Image.LoadAsync(fileStream, ct);

        if (image.Width > MaxWidth)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxWidth, 0)
            }));
        }

        await image.SaveAsJpegAsync(destinationPath, new JpegEncoder { Quality = JpegQuality }, ct);
    }

    // ═══════════════════════════════════════════
    //  INDEX SYNC
    // ═══════════════════════════════════════════

    /// <summary>
    /// Bring the stores.StoreItemImage rows for these items in line with what is on disk. The
    /// table is an index, not a second source of truth, so disk always wins. DisplayOrder carries
    /// the instance number, which is what orders the catalog carousel.
    ///
    /// <para>
    /// This is also the repair path for the drift already in the data: the index held only each
    /// item's first instance, so items with two or three photos showed one in the new catalog.
    /// Opening the images screen or touching any image now reconciles them.
    /// </para>
    /// </summary>
    private async Task SyncIndexAsync(
        int storeId, IReadOnlyList<StoreItemKeyDto> items, string userId, CancellationToken ct)
    {
        var ids = items.Select(i => i.StoreItemId).ToList();
        var existingRows = await _itemRepo.GetImageRowsForItemsAsync(ids, ct);

        var desired = items
            .SelectMany(item => ReadInstances(storeId, item.StoreItemId)
                .Select(instance => (
                    item.StoreItemId,
                    Instance: instance,
                    Url: PlainUrl(BuildFileName(storeId, item.StoreItemId, instance)))))
            .ToList();

        static bool Matches((int StoreItemId, int Instance, string Url) d, StoreItemImage row) =>
            d.StoreItemId == row.StoreItemId
            && d.Instance == row.DisplayOrder
            && string.Equals(d.Url, row.ImageUrl, StringComparison.OrdinalIgnoreCase);

        var stale = existingRows.Where(row => !desired.Any(d => Matches(d, row))).ToList();
        var missing = desired.Where(d => !existingRows.Any(row => Matches(d, row))).ToList();

        if (stale.Count == 0 && missing.Count == 0) return;

        _itemRepo.RemoveImageRows(stale);
        _itemRepo.AddImageRows(missing.Select(d => new StoreItemImage
        {
            StoreItemId = d.StoreItemId,
            ImageUrl = d.Url,
            DisplayOrder = d.Instance,
            Modified = DateTime.Now,
            LebUserId = userId
        }));

        await _itemRepo.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════

    private async Task<int> ResolveStoreIdAsync(Guid jobId, CancellationToken ct)
    {
        var store = await _storeRepo.GetByJobIdAsync(jobId, ct)
            ?? throw new InvalidOperationException("Store not found for this job.");
        return store.StoreId;
    }

    /// <summary>
    /// StoreItemId is not job-scoped, so confirm the item belongs to the caller's store before
    /// reading or writing files named after it.
    /// </summary>
    private async Task<StoreItemKeyDto> AssertItemInStoreAsync(
        int storeId, int storeItemId, CancellationToken ct)
    {
        var items = await _itemRepo.GetItemKeysForStoreAsync(storeId, ct);
        return items.FirstOrDefault(i => i.StoreItemId == storeItemId)
            ?? throw new InvalidOperationException("Item not found in this job's store.");
    }

    /// <summary>The stored, canonical URL — no cache-buster, because it is persisted.</summary>
    private string PlainUrl(string fileName) => $"{_staticsBaseUrl}/{UrlFolder}/{fileName}";

    /// <summary>
    /// LEGACY AddCacheBuster. A replaced image keeps its filename, so without this the browser
    /// serves the old bytes and the director thinks the upload failed. Applied to what the admin
    /// screen renders, never to what is written to the index.
    /// </summary>
    private string CacheBustedUrl(string fileName) =>
        $"{PlainUrl(fileName)}?v={DateTime.UtcNow.Ticks}";

    private StoreItemImageDto ToDto(int storeId, StoreItemKeyDto item, int instance)
    {
        var fileName = BuildFileName(storeId, item.StoreItemId, instance);
        return new StoreItemImageDto
        {
            StoreItemId = item.StoreItemId,
            StoreItemName = item.StoreItemName,
            Instance = instance,
            FileName = fileName,
            ImageUrl = CacheBustedUrl(fileName),
            IsPlaceholder = false
        };
    }

    private StoreItemImageDto PlaceholderRow(StoreItemKeyDto item) => new()
    {
        StoreItemId = item.StoreItemId,
        StoreItemName = item.StoreItemName,
        Instance = 0,
        FileName = MissingImageFile,
        ImageUrl = PlainUrl(MissingImageFile),
        IsPlaceholder = true
    };
}
