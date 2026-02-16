using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
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

    public async Task<List<WidgetItemProjection>> GetDefaultsAsync(
        int jobTypeId,
        string roleId,
        CancellationToken ct = default)
    {
        return await _context.WidgetDefault
            .AsNoTracking()
            .Where(wd => wd.JobTypeId == jobTypeId && wd.RoleId == roleId)
            .OrderBy(wd => wd.Category.Section)
            .ThenBy(wd => wd.Category.DefaultOrder)
            .ThenBy(wd => wd.DisplayOrder)
            .Select(wd => new WidgetItemProjection
            {
                WidgetId = wd.WidgetId,
                CategoryId = wd.CategoryId,
                DisplayOrder = wd.DisplayOrder,
                Config = wd.Config,
                IsEnabled = true,
                WidgetName = wd.Widget.Name,
                WidgetType = wd.Widget.WidgetType,
                ComponentKey = wd.Widget.ComponentKey,
                Description = wd.Widget.Description,
                CategoryName = wd.Category.Name,
                CategoryIcon = wd.Category.Icon,
                CategoryDefaultOrder = wd.Category.DefaultOrder,
                Section = wd.Category.Section
            })
            .ToListAsync(ct);
    }

    public async Task<List<WidgetItemProjection>> GetJobWidgetsAsync(
        Guid jobId,
        string roleId,
        CancellationToken ct = default)
    {
        return await _context.JobWidget
            .AsNoTracking()
            .Where(jw => jw.JobId == jobId && jw.RoleId == roleId)
            .OrderBy(jw => jw.Category.Section)
            .ThenBy(jw => jw.Category.DefaultOrder)
            .ThenBy(jw => jw.DisplayOrder)
            .Select(jw => new WidgetItemProjection
            {
                WidgetId = jw.WidgetId,
                CategoryId = jw.CategoryId,
                DisplayOrder = jw.DisplayOrder,
                Config = jw.Config,
                IsEnabled = jw.IsEnabled,
                WidgetName = jw.Widget.Name,
                WidgetType = jw.Widget.WidgetType,
                ComponentKey = jw.Widget.ComponentKey,
                Description = jw.Widget.Description,
                CategoryName = jw.Category.Name,
                CategoryIcon = jw.Category.Icon,
                CategoryDefaultOrder = jw.Category.DefaultOrder,
                Section = jw.Category.Section
            })
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
