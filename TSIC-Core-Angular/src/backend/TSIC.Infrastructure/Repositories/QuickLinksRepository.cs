using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for the quicklinks catalog (LinkType) and per-job overrides
/// (JobQuickLink). Mirrors the NavEditor/WidgetEditor repository house style.
/// </summary>
public class QuickLinksRepository : IQuickLinksRepository
{
    private readonly SqlDbContext _context;

    public QuickLinksRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default)
    {
        return await _context.JobTypes
            .AsNoTracking()
            .OrderBy(jt => jt.JobTypeName)
            .Select(jt => new JobTypeRefDto
            {
                JobTypeId = jt.JobTypeId,
                JobTypeName = jt.JobTypeName ?? $"JobType {jt.JobTypeId}",
            })
            .ToListAsync(ct);
    }

    public async Task<List<JobRefDto>> GetJobsByJobTypeAsync(int jobTypeId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobTypeId == jobTypeId)
            .OrderByDescending(j => j.ExpiryAdmin)
            .Select(j => new JobRefDto
            {
                JobId = j.JobId,
                JobName = j.JobName ?? j.JobPath,
                JobPath = j.JobPath,
            })
            .ToListAsync(ct);
    }

    public async Task<JobRefDto?> GetJobRefAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobRefDto
            {
                JobId = j.JobId,
                JobName = j.JobName ?? j.JobPath,
                JobPath = j.JobPath,
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<LinkType>> GetActiveLinkTypesAsync(CancellationToken ct = default)
    {
        return await _context.LinkType
            .AsNoTracking()
            .Where(lt => lt.Active)
            .OrderBy(lt => lt.DefaultSortOrder)
            .ToListAsync(ct);
    }

    public async Task<List<JobQuickLink>> GetJobQuickLinksByJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobQuickLink
            .AsNoTracking()
            .Where(q => q.JobId == jobId)
            .ToListAsync(ct);
    }

    public async Task<JobQuickLink?> GetJobQuickLinkAsync(Guid jobId, string linkKey, CancellationToken ct = default)
    {
        return await _context.JobQuickLink
            .FirstOrDefaultAsync(q => q.JobId == jobId && q.LinkKey == linkKey, ct);
    }

    public void AddJobQuickLink(JobQuickLink row) => _context.JobQuickLink.Add(row);

    public void RemoveJobQuickLink(JobQuickLink row) => _context.JobQuickLink.Remove(row);

    public async Task SaveChangesAsync(CancellationToken ct = default) => await _context.SaveChangesAsync(ct);
}
