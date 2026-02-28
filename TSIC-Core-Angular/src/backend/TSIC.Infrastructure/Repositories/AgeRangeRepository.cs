using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.AgeRange;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class AgeRangeRepository : IAgeRangeRepository
{
    private readonly SqlDbContext _context;

    public AgeRangeRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<AgeRangeDto>> GetAllForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobAgeRanges
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderBy(r => r.RangeLeft)
            .Select(r => new AgeRangeDto
            {
                AgeRangeId = r.AgeRangeId,
                RangeName = r.RangeName ?? string.Empty,
                RangeLeft = r.RangeLeft,
                RangeRight = r.RangeRight,
                Modified = r.Modified,
                ModifiedByUsername = r.LebUser != null
                    ? r.LebUser.UserName
                    : null
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<JobAgeRanges?> GetByIdAsync(
        int ageRangeId,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobAgeRanges
            .FirstOrDefaultAsync(r => r.AgeRangeId == ageRangeId, cancellationToken);
    }

    public async Task<bool> ExistsWithNameAsync(
        Guid jobId,
        string rangeName,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.JobAgeRanges
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .Where(r => r.RangeName != null && r.RangeName.ToLower() == rangeName.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(r => r.AgeRangeId != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<(bool Overlaps, string? OverlappingName)> HasOverlapAsync(
        Guid jobId,
        DateTime rangeLeft,
        DateTime rangeRight,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.JobAgeRanges
            .AsNoTracking()
            .Where(r => r.JobId == jobId);

        if (excludeId.HasValue)
        {
            query = query.Where(r => r.AgeRangeId != excludeId.Value);
        }

        // Standard overlap check: two ranges overlap when left1 <= right2 AND right1 >= left2
        var overlapping = await query
            .Where(r => rangeLeft <= r.RangeRight && rangeRight >= r.RangeLeft)
            .Select(r => r.RangeName)
            .FirstOrDefaultAsync(cancellationToken);

        return overlapping != null
            ? (true, overlapping)
            : (false, null);
    }

    public void Add(JobAgeRanges entity)
    {
        _context.JobAgeRanges.Add(entity);
    }

    public void Remove(JobAgeRanges entity)
    {
        _context.JobAgeRanges.Remove(entity);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
