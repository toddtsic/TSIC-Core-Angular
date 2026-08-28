using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Store;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Store;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Stores entity and related lookup tables (Colors, Sizes).
/// </summary>
public class StoreRepository : IStoreRepository
{
    private readonly SqlDbContext _context;

    public StoreRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Store ──

    public async Task<Stores?> GetByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.JobId == jobId, cancellationToken);
    }

    public void Add(Stores store)
    {
        _context.Stores.Add(store);
    }

    public async Task<JobStoreConfig?> GetJobStoreConfigAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => new JobStoreConfig
            {
                StoreSalesTax = j.StoreSalesTax,
                ProcessingFeePercent = j.ProcessingFeePercent,
                CustomerAi = j.Customer.CustomerAi,
                JobAi = j.JobAi
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StoreFrontInfoDto> GetStoreFrontInfoAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var row = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                j.StorePickupDetails,
                j.StoreRefundPolicy,
                j.StoreContactEmail
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        // Blank-but-present is the same as absent to a shopper, so trim to null here rather than
        // making every consumer test for whitespace.
        static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var pickup = Clean(row?.StorePickupDetails);
        var refund = Clean(row?.StoreRefundPolicy);
        var contact = Clean(row?.StoreContactEmail);

        return new StoreFrontInfoDto
        {
            PickupDetails = pickup,
            RefundPolicy = refund,
            ContactEmail = contact,
            HasAny = pickup is not null || refund is not null || contact is not null
        };
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    // ── Colors ──

    public async Task<List<StoreColors>> GetAllColorsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StoreColors
            .OrderBy(c => c.StoreColorName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<StoreColors?> GetColorByIdAsync(int storeColorId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreColors
            .FirstOrDefaultAsync(c => c.StoreColorId == storeColorId, cancellationToken);
    }

    public async Task<StoreColors?> GetColorByNameAsync(string storeColorName, CancellationToken cancellationToken = default)
    {
        return await _context.StoreColors
            .FirstOrDefaultAsync(c => c.StoreColorName == storeColorName, cancellationToken);
    }

    public void AddColor(StoreColors color)
    {
        _context.StoreColors.Add(color);
    }

    public async Task<bool> IsColorInUseAsync(int storeColorId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItemSkus
            .AsNoTracking()
            .AnyAsync(s => s.StoreColorId == storeColorId, cancellationToken);
    }

    public void RemoveColor(StoreColors color)
    {
        _context.StoreColors.Remove(color);
    }

    // ── Sizes ──

    /// <summary>
    /// Size order, not alphabetical order - the list feeds the SKU-builder pickers as well as the
    /// Sizes screen, and "Adult Large, Adult Medium, Adult Small, Adult XL" is not a size list.
    /// The sort is in memory because <see cref="StoreSizeOrder"/> cannot be translated to SQL;
    /// the table is a single global lookup of a few dozen rows.
    /// </summary>
    public async Task<List<StoreSizes>> GetAllSizesAsync(CancellationToken cancellationToken = default)
    {
        var sizes = await _context.StoreSizes
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. sizes.OrderBy(s => StoreSizeOrder.Key(s.StoreSizeName))];
    }

    public async Task<StoreSizes?> GetSizeByIdAsync(int storeSizeId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreSizes
            .FirstOrDefaultAsync(s => s.StoreSizeId == storeSizeId, cancellationToken);
    }

    public async Task<StoreSizes?> GetSizeByNameAsync(string storeSizeName, CancellationToken cancellationToken = default)
    {
        return await _context.StoreSizes
            .FirstOrDefaultAsync(s => s.StoreSizeName == storeSizeName, cancellationToken);
    }

    public void AddSize(StoreSizes size)
    {
        _context.StoreSizes.Add(size);
    }

    public async Task<bool> IsSizeInUseAsync(int storeSizeId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreItemSkus
            .AsNoTracking()
            .AnyAsync(s => s.StoreSizeId == storeSizeId, cancellationToken);
    }

    public void RemoveSize(StoreSizes size)
    {
        _context.StoreSizes.Remove(size);
    }
}
