using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for per-job Team Events feature switches (<c>teamevents.JobFeatures</c>).
/// </summary>
public class JobFeaturesRepository : IJobFeaturesRepository
{
    private readonly SqlDbContext _context;

    public JobFeaturesRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<JobFeatures?> GetByJobIdAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobFeatures
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.JobId == jobId, ct);
    }

    public async Task<JobFeatures?> GetTrackedAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobFeatures
            .FirstOrDefaultAsync(f => f.JobId == jobId, ct);
    }

    public void Add(JobFeatures features)
    {
        _context.JobFeatures.Add(features);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
