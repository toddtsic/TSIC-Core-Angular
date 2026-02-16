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

    public async Task<DashboardMetricsDto> GetDashboardMetricsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Registration + financial aggregates — single GroupBy query
        var regStats = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .GroupBy(r => 1)
            .Select(g => new
            {
                TotalActive = g.Count(r => r.BActive == true),
                TotalInactive = g.Count(r => r.BActive != true),
                TotalFees = g.Where(r => r.BActive == true).Sum(r => r.FeeTotal),
                TotalPaid = g.Where(r => r.BActive == true).Sum(r => r.PaidTotal),
                TotalOwed = g.Where(r => r.BActive == true).Sum(r => r.OwedTotal),
                PaidInFull = g.Count(r => r.BActive == true && r.OwedTotal == 0),
                Underpaid = g.Count(r => r.BActive == true && r.OwedTotal > 0),
            })
            .FirstOrDefaultAsync(ct);

        // Team count — sequential (shared DbContext)
        var teamCount = await _context.Teams
            .AsNoTracking()
            .CountAsync(t => t.JobId == jobId && t.Active == true, ct);

        // Club count — distinct ClubName from active registrations
        var clubCount = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true && r.ClubName != null && r.ClubName != "")
            .Select(r => r.ClubName)
            .Distinct()
            .CountAsync(ct);

        return new DashboardMetricsDto
        {
            Registrations = new RegistrationMetrics
            {
                TotalActive = regStats?.TotalActive ?? 0,
                TotalInactive = regStats?.TotalInactive ?? 0,
                Teams = teamCount,
                Clubs = clubCount,
            },
            Financials = new FinancialMetrics
            {
                TotalFees = regStats?.TotalFees ?? 0,
                TotalPaid = regStats?.TotalPaid ?? 0,
                TotalOwed = regStats?.TotalOwed ?? 0,
                PaidInFull = regStats?.PaidInFull ?? 0,
                Underpaid = regStats?.Underpaid ?? 0,
            },
            Scheduling = new SchedulingMetrics
            {
                TotalAgegroups = 0,
                AgegroupsScheduled = 0,
                FieldCount = 0,
                TotalDivisions = 0,
            }
        };
    }
}
