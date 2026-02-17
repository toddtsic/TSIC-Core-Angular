using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for the SuperUser widget editor — manages widget definitions and default assignments.
/// </summary>
public class WidgetEditorRepository : IWidgetEditorRepository
{
    private readonly SqlDbContext _context;

    /// <summary>
    /// Dashboard-relevant roles shown as matrix columns.
    /// </summary>
    private static readonly string[] DashboardRoleIds =
    [
        RoleConstants.Anonymous,
        RoleConstants.Superuser,
        RoleConstants.SuperDirector,
        RoleConstants.Director,
        RoleConstants.ClubRep,
        RoleConstants.Player,
        RoleConstants.Staff,
        RoleConstants.Guest,
    ];

    public WidgetEditorRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ══════════════════════════════════════
    // Reference data
    // ══════════════════════════════════════

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

    public async Task<List<RoleRefDto>> GetRolesAsync(CancellationToken ct = default)
    {
        return await _context.AspNetRoles
            .AsNoTracking()
            .Where(r => DashboardRoleIds.Contains(r.Id))
            .OrderBy(r => r.Name)
            .Select(r => new RoleRefDto
            {
                RoleId = r.Id,
                RoleName = r.Name ?? r.Id,
            })
            .ToListAsync(ct);
    }

    public async Task<List<WidgetCategoryRefDto>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await _context.WidgetCategory
            .AsNoTracking()
            .OrderBy(c => c.Workspace)
            .ThenBy(c => c.DefaultOrder)
            .Select(c => new WidgetCategoryRefDto
            {
                CategoryId = c.CategoryId,
                Name = c.Name,
                Workspace = c.Workspace,
                Icon = c.Icon,
                DefaultOrder = c.DefaultOrder,
            })
            .ToListAsync(ct);
    }

    // ══════════════════════════════════════
    // Widget definitions
    // ══════════════════════════════════════

    public async Task<List<WidgetDefinitionDto>> GetWidgetDefinitionsAsync(CancellationToken ct = default)
    {
        return await _context.Widget
            .AsNoTracking()
            .OrderBy(w => w.Category.Workspace)
            .ThenBy(w => w.Category.DefaultOrder)
            .ThenBy(w => w.Name)
            .Select(w => new WidgetDefinitionDto
            {
                WidgetId = w.WidgetId,
                Name = w.Name,
                WidgetType = w.WidgetType,
                ComponentKey = w.ComponentKey,
                CategoryId = w.CategoryId,
                Description = w.Description,
                CategoryName = w.Category.Name,
                Workspace = w.Category.Workspace,
            })
            .ToListAsync(ct);
    }

    public async Task<Widget?> GetWidgetByIdAsync(int widgetId, CancellationToken ct = default)
    {
        return await _context.Widget
            .FirstOrDefaultAsync(w => w.WidgetId == widgetId, ct);
    }

    public async Task<WidgetDefinitionDto?> GetWidgetDefinitionByIdAsync(int widgetId, CancellationToken ct = default)
    {
        return await _context.Widget
            .AsNoTracking()
            .Where(w => w.WidgetId == widgetId)
            .Select(w => new WidgetDefinitionDto
            {
                WidgetId = w.WidgetId,
                Name = w.Name,
                WidgetType = w.WidgetType,
                ComponentKey = w.ComponentKey,
                CategoryId = w.CategoryId,
                Description = w.Description,
                CategoryName = w.Category.Name,
                Workspace = w.Category.Workspace,
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> ComponentKeyExistsAsync(string componentKey, int? excludeWidgetId = null, CancellationToken ct = default)
    {
        var query = _context.Widget.AsNoTracking().Where(w => w.ComponentKey == componentKey);
        if (excludeWidgetId.HasValue)
            query = query.Where(w => w.WidgetId != excludeWidgetId.Value);
        return await query.AnyAsync(ct);
    }

    public void AddWidget(Widget widget)
    {
        _context.Widget.Add(widget);
    }

    public void RemoveWidget(Widget widget)
    {
        _context.Widget.Remove(widget);
    }

    public async Task<bool> WidgetHasDependenciesAsync(int widgetId, CancellationToken ct = default)
    {
        var hasDefaults = await _context.WidgetDefault
            .AsNoTracking()
            .AnyAsync(wd => wd.WidgetId == widgetId, ct);

        if (hasDefaults) return true;

        var hasJobWidgets = await _context.JobWidget
            .AsNoTracking()
            .AnyAsync(jw => jw.WidgetId == widgetId, ct);

        return hasJobWidgets;
    }

    // ══════════════════════════════════════
    // Widget defaults matrix
    // ══════════════════════════════════════

    public async Task<List<WidgetDefaultEntryDto>> GetDefaultsByJobTypeAsync(int jobTypeId, CancellationToken ct = default)
    {
        return await _context.WidgetDefault
            .AsNoTracking()
            .Where(wd => wd.JobTypeId == jobTypeId)
            .Select(wd => new WidgetDefaultEntryDto
            {
                WidgetId = wd.WidgetId,
                RoleId = wd.RoleId,
                CategoryId = wd.CategoryId,
                DisplayOrder = wd.DisplayOrder,
                Config = wd.Config,
            })
            .ToListAsync(ct);
    }

    public async Task<List<WidgetDefault>> GetDefaultEntitiesByJobTypeAsync(int jobTypeId, CancellationToken ct = default)
    {
        return await _context.WidgetDefault
            .Where(wd => wd.JobTypeId == jobTypeId)
            .ToListAsync(ct);
    }

    public void RemoveDefaults(List<WidgetDefault> defaults)
    {
        _context.WidgetDefault.RemoveRange(defaults);
    }

    public async Task BulkInsertDefaultsAsync(int jobTypeId, List<WidgetDefaultEntryDto> entries, CancellationToken ct = default)
    {
        var entities = entries.Select(e => new WidgetDefault
        {
            JobTypeId = jobTypeId,
            WidgetId = e.WidgetId,
            RoleId = e.RoleId,
            CategoryId = e.CategoryId,
            DisplayOrder = e.DisplayOrder,
            Config = e.Config,
        });

        await _context.WidgetDefault.AddRangeAsync(entities, ct);
    }

    // ══════════════════════════════════════
    // Widget-centric assignments
    // ══════════════════════════════════════

    public async Task<List<WidgetAssignmentDto>> GetAssignmentsByWidgetAsync(int widgetId, CancellationToken ct = default)
    {
        return await _context.WidgetDefault
            .AsNoTracking()
            .Where(wd => wd.WidgetId == widgetId)
            .Select(wd => new WidgetAssignmentDto
            {
                JobTypeId = wd.JobTypeId,
                RoleId = wd.RoleId,
            })
            .ToListAsync(ct);
    }

    public async Task<List<WidgetDefault>> GetDefaultEntitiesByWidgetAsync(int widgetId, CancellationToken ct = default)
    {
        return await _context.WidgetDefault
            .Where(wd => wd.WidgetId == widgetId)
            .ToListAsync(ct);
    }

    public async Task BulkInsertAssignmentsAsync(int widgetId, int categoryId, List<WidgetAssignmentDto> assignments, CancellationToken ct = default)
    {
        // Compute max DisplayOrder per jobType so new assignments append at the end
        var jobTypeIds = assignments.Select(a => a.JobTypeId).Distinct().ToList();
        var maxOrders = await _context.WidgetDefault
            .AsNoTracking()
            .Where(wd => jobTypeIds.Contains(wd.JobTypeId) && wd.CategoryId == categoryId)
            .GroupBy(wd => wd.JobTypeId)
            .Select(g => new { JobTypeId = g.Key, MaxOrder = g.Max(wd => wd.DisplayOrder) })
            .ToDictionaryAsync(x => x.JobTypeId, x => x.MaxOrder, ct);

        var entities = assignments.Select(a => new WidgetDefault
        {
            WidgetId = widgetId,
            JobTypeId = a.JobTypeId,
            RoleId = a.RoleId,
            CategoryId = categoryId,
            DisplayOrder = maxOrders.GetValueOrDefault(a.JobTypeId, -1) + 1,
        });

        await _context.WidgetDefault.AddRangeAsync(entities, ct);
    }

    // ══════════════════════════════════════
    // Per-job overrides
    // ══════════════════════════════════════

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

    public async Task<List<JobWidgetEntryDto>> GetJobWidgetsByJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobWidget
            .AsNoTracking()
            .Where(jw => jw.JobId == jobId)
            .Select(jw => new JobWidgetEntryDto
            {
                WidgetId = jw.WidgetId,
                RoleId = jw.RoleId,
                CategoryId = jw.CategoryId,
                DisplayOrder = jw.DisplayOrder,
                Config = jw.Config,
                IsEnabled = jw.IsEnabled,
                IsOverridden = true,
            })
            .ToListAsync(ct);
    }

    public async Task<List<JobWidget>> GetJobWidgetEntitiesAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobWidget
            .Where(jw => jw.JobId == jobId)
            .ToListAsync(ct);
    }

    public void RemoveJobWidgets(List<JobWidget> jobWidgets)
    {
        _context.JobWidget.RemoveRange(jobWidgets);
    }

    public async Task BulkInsertJobWidgetsAsync(Guid jobId, List<JobWidgetEntryDto> entries, CancellationToken ct = default)
    {
        var entities = entries.Select(e => new JobWidget
        {
            JobId = jobId,
            WidgetId = e.WidgetId,
            RoleId = e.RoleId,
            CategoryId = e.CategoryId,
            DisplayOrder = e.DisplayOrder,
            IsEnabled = e.IsEnabled,
            Config = e.Config,
        });

        await _context.JobWidget.AddRangeAsync(entities, ct);
    }

    public async Task<int?> GetJobTypeIdForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (int?)j.JobTypeId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
