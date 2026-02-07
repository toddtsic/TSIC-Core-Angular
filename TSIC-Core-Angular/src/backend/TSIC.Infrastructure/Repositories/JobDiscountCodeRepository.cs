using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IJobDiscountCodeRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for JobDiscountCodes entity.
/// </summary>
public class JobDiscountCodeRepository : IJobDiscountCodeRepository
{
    private readonly SqlDbContext _context;

    public JobDiscountCodeRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<(bool? BAsPercent, decimal? CodeAmount)?> GetActiveCodeAsync(
        Guid jobId,
        string codeNameLower,
        DateTime currentTime,
        CancellationToken cancellationToken = default)
    {
        var result = await _context.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.JobId == jobId
                        && d.Active
                        && d.CodeStartDate <= currentTime
                        && d.CodeEndDate >= currentTime
                        && d.CodeName.ToLower() == codeNameLower)
            .Select(d => new { d.BAsPercent, d.CodeAmount })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null ? (result.BAsPercent, result.CodeAmount) : null;
    }

    public async Task<(bool? BAsPercent, decimal? CodeAmount)?> GetByAiAsync(int discountCodeAi, CancellationToken cancellationToken = default)
    {
        var result = await _context.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.Ai == discountCodeAi)
            .Select(d => new { d.BAsPercent, d.CodeAmount })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null ? (result.BAsPercent, result.CodeAmount) : null;
    }

    public async Task<List<JobDiscountCodes>> GetActiveCodesForJobAsync(
        Guid jobId,
        DateTime currentTime,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.JobId == jobId
                && d.Active
                && d.CodeStartDate <= currentTime
                && d.CodeEndDate >= currentTime)
            .ToListAsync(cancellationToken);
    }

    // === ADMIN MANAGEMENT METHODS ===

    public async Task<List<JobDiscountCodes>> GetAllByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobDiscountCodes
            .AsNoTracking()
            .Where(d => d.JobId == jobId)
            .OrderByDescending(d => d.Modified)
            .ToListAsync(cancellationToken);
    }

    public async Task<JobDiscountCodes?> GetByIdAsync(
        int ai,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobDiscountCodes
            .FirstOrDefaultAsync(d => d.Ai == ai, cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(
        Guid jobId,
        string codeName,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobDiscountCodes
            .AsNoTracking()
            .AnyAsync(d => d.JobId == jobId && d.CodeName.ToLower() == codeName.ToLower(), cancellationToken);
    }

    public async Task<int> GetUsageCountAsync(
        int ai,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .CountAsync(r => r.DiscountCodeId == ai, cancellationToken);
    }

    public void Add(JobDiscountCodes code)
    {
        _context.JobDiscountCodes.Add(code);
    }

    public void Remove(JobDiscountCodes code)
    {
        _context.JobDiscountCodes.Remove(code);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
