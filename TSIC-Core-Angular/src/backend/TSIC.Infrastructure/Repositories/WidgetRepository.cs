using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for widget dashboard data access.
/// </summary>
public class WidgetRepository : IWidgetRepository
{
    private readonly SqlDbContext _context;

    public WidgetRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<WidgetDefault>> GetDefaultsAsync(
        int jobTypeId,
        string roleId,
        CancellationToken ct = default)
    {
        return await _context.WidgetDefault
            .AsNoTracking()
            .Include(wd => wd.Widget)
            .Include(wd => wd.Category)
            .Where(wd => wd.JobTypeId == jobTypeId && wd.RoleId == roleId)
            .OrderBy(wd => wd.Category.Section)
            .ThenBy(wd => wd.Category.DefaultOrder)
            .ThenBy(wd => wd.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<List<JobWidget>> GetJobWidgetsAsync(
        Guid jobId,
        string roleId,
        CancellationToken ct = default)
    {
        return await _context.JobWidget
            .AsNoTracking()
            .Include(jw => jw.Widget)
            .Include(jw => jw.Category)
            .Where(jw => jw.JobId == jobId && jw.RoleId == roleId)
            .OrderBy(jw => jw.Category.Section)
            .ThenBy(jw => jw.Category.DefaultOrder)
            .ThenBy(jw => jw.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<int?> GetJobTypeIdAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (int?)j.JobTypeId)
            .FirstOrDefaultAsync(ct);
    }
}
