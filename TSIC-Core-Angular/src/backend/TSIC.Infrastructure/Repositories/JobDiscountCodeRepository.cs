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
}
