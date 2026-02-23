using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
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
                StoreTsicrate = j.StoreTsicrate ?? 0m
            })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
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

    public async Task<List<StoreSizes>> GetAllSizesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StoreSizes
            .OrderBy(s => s.StoreSizeName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<StoreSizes?> GetSizeByIdAsync(int storeSizeId, CancellationToken cancellationToken = default)
    {
        return await _context.StoreSizes
            .FirstOrDefaultAsync(s => s.StoreSizeId == storeSizeId, cancellationToken);
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
