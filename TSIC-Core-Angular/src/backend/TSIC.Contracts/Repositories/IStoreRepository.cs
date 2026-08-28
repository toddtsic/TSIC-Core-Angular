using TSIC.Contracts.Dtos.Store;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Store-related fee/tax config from the Jobs table.
/// </summary>
public record JobStoreConfig
{
    public required decimal StoreSalesTax { get; init; }

    /// <summary>
    /// Raw Jobs.ProcessingFeePercent (nullable). Convert via
    /// ProcessingRateMath.ToCcMultiplier — never divide by 100 directly.
    /// </summary>
    public required decimal? ProcessingFeePercent { get; init; }

    /// <summary>
    /// Customers.CustomerAi — first segment of the ADN invoice number.
    /// </summary>
    public required int CustomerAi { get; init; }

    /// <summary>
    /// Jobs.JobAi — second segment of the ADN invoice number.
    /// </summary>
    public required int JobAi { get; init; }
}

/// <summary>
/// Repository for the Stores entity and related lookup tables (Colors, Sizes).
/// </summary>
public interface IStoreRepository
{
    // ── Store ──

    /// <summary>
    /// Get the store for a job. Returns null if no store exists.
    /// </summary>
    Task<Stores?> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get store-related fee/tax config from the Jobs table.
    /// </summary>
    Task<JobStoreConfig?> GetJobStoreConfigAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The director's shopper-facing store copy: pickup details, refund policy, contact email.
    /// Kept off <see cref="JobStoreConfig"/> deliberately — that record is the money/ADN config
    /// and has no business growing a tail of display text.
    /// </summary>
    Task<StoreFrontInfoDto> GetStoreFrontInfoAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new store for a job.
    /// </summary>
    void Add(Stores store);

    /// <summary>
    /// Persist all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // ── Colors ──

    /// <summary>
    /// Get all store colors, ordered by name.
    /// </summary>
    Task<List<StoreColors>> GetAllColorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single color by ID (tracked for updates).
    /// </summary>
    Task<StoreColors?> GetColorByIdAsync(int storeColorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find a colour by name. StoreColors is a GLOBAL dictionary - no store or job scoping -
    /// matching legacy ProcessColorsAsync, which reuses any existing name across all customers.
    /// </summary>
    Task<StoreColors?> GetColorByNameAsync(string storeColorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new color.
    /// </summary>
    void AddColor(StoreColors color);

    /// <summary>
    /// Check if a color is referenced by any SKU.
    /// </summary>
    Task<bool> IsColorInUseAsync(int storeColorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a color entity.
    /// </summary>
    void RemoveColor(StoreColors color);

    // ── Sizes ──

    /// <summary>
    /// Get all store sizes, ordered by name.
    /// </summary>
    Task<List<StoreSizes>> GetAllSizesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single size by ID (tracked for updates).
    /// </summary>
    Task<StoreSizes?> GetSizeByIdAsync(int storeSizeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find a size by name. StoreSizes is a GLOBAL dictionary - see GetColorByNameAsync.
    /// </summary>
    Task<StoreSizes?> GetSizeByNameAsync(string storeSizeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new size.
    /// </summary>
    void AddSize(StoreSizes size);

    /// <summary>
    /// Check if a size is referenced by any SKU.
    /// </summary>
    Task<bool> IsSizeInUseAsync(int storeSizeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a size entity.
    /// </summary>
    void RemoveSize(StoreSizes size);
}
