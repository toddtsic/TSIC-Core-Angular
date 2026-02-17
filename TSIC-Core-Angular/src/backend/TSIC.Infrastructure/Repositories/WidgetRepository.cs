using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
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
            .OrderBy(wd => wd.Category.Workspace)
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
                Workspace = wd.Category.Workspace
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
            .OrderBy(jw => jw.Category.Workspace)
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
                Workspace = jw.Category.Workspace
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

    public async Task<RegistrationTimeSeriesDto> GetRegistrationTimeSeriesAsync(Guid jobId, CancellationToken ct = default)
    {
        // Daily aggregates — group active registrations by date
        var dailyRaw = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true)
            .GroupBy(r => r.RegistrationTs.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(r => r.PaidTotal),
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        // Summary aggregates — single pass
        var summary = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                TotalRevenue = g.Sum(r => r.PaidTotal),
                TotalOutstanding = g.Sum(r => r.OwedTotal),
                PaidInFull = g.Count(r => r.OwedTotal == 0),
                Underpaid = g.Count(r => r.OwedTotal > 0),
            })
            .FirstOrDefaultAsync(ct);

        // Build cumulative totals in-memory (cheap — daily buckets are small)
        var cumulativeCount = 0;
        var cumulativeRevenue = 0m;
        var dailyData = dailyRaw.Select(d =>
        {
            cumulativeCount += d.Count;
            cumulativeRevenue += d.Revenue;
            return new DailyRegistrationPointDto
            {
                Date = d.Date,
                Count = d.Count,
                CumulativeCount = cumulativeCount,
                Revenue = d.Revenue,
                CumulativeRevenue = cumulativeRevenue,
            };
        }).ToList();

        return new RegistrationTimeSeriesDto
        {
            DailyData = dailyData,
            Summary = new RegistrationTrendSummaryDto
            {
                TotalRegistrations = summary?.Total ?? 0,
                TotalRevenue = summary?.TotalRevenue ?? 0,
                TotalOutstanding = summary?.TotalOutstanding ?? 0,
                PaidInFull = summary?.PaidInFull ?? 0,
                Underpaid = summary?.Underpaid ?? 0,
            }
        };
    }

    public async Task<RegistrationTimeSeriesDto> GetPlayerTimeSeriesAsync(Guid jobId, CancellationToken ct = default)
    {
        // Daily player registration aggregates — RoleId = Player, active only
        var dailyRaw = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true && r.RoleId == RoleConstants.Player)
            .GroupBy(r => r.RegistrationTs.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(r => r.PaidTotal),
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        // Summary aggregates for players
        var summary = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true && r.RoleId == RoleConstants.Player)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                TotalRevenue = g.Sum(r => r.PaidTotal),
                TotalOutstanding = g.Sum(r => r.OwedTotal),
                PaidInFull = g.Count(r => r.OwedTotal == 0),
                Underpaid = g.Count(r => r.OwedTotal > 0),
            })
            .FirstOrDefaultAsync(ct);

        var cumulativeCount = 0;
        var cumulativeRevenue = 0m;
        var dailyData = dailyRaw.Select(d =>
        {
            cumulativeCount += d.Count;
            cumulativeRevenue += d.Revenue;
            return new DailyRegistrationPointDto
            {
                Date = d.Date,
                Count = d.Count,
                CumulativeCount = cumulativeCount,
                Revenue = d.Revenue,
                CumulativeRevenue = cumulativeRevenue,
            };
        }).ToList();

        return new RegistrationTimeSeriesDto
        {
            DailyData = dailyData,
            Summary = new RegistrationTrendSummaryDto
            {
                TotalRegistrations = summary?.Total ?? 0,
                TotalRevenue = summary?.TotalRevenue ?? 0,
                TotalOutstanding = summary?.TotalOutstanding ?? 0,
                PaidInFull = summary?.PaidInFull ?? 0,
                Underpaid = summary?.Underpaid ?? 0,
            }
        };
    }

    public async Task<RegistrationTimeSeriesDto> GetTeamTimeSeriesAsync(Guid jobId, CancellationToken ct = default)
    {
        // Daily team aggregates — teams with ClubRep payment, active only
        // Uses Teams.Createdate for timing, Teams financial fields for revenue
        var dailyRaw = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true && t.ClubrepRegistrationid != null)
            .GroupBy(t => t.Createdate.Date)
            .Select(g => new
            {
                Date = g.Key,
                Count = g.Count(),
                Revenue = g.Sum(t => t.PaidTotal ?? 0m),
            })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        // Summary aggregates for teams
        var summary = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true && t.ClubrepRegistrationid != null)
            .GroupBy(t => 1)
            .Select(g => new
            {
                Total = g.Count(),
                TotalRevenue = g.Sum(t => t.PaidTotal ?? 0m),
                TotalOutstanding = g.Sum(t => t.OwedTotal ?? 0m),
                PaidInFull = g.Count(t => (t.OwedTotal ?? 0m) == 0m),
                Underpaid = g.Count(t => (t.OwedTotal ?? 0m) > 0m),
            })
            .FirstOrDefaultAsync(ct);

        var cumulativeCount = 0;
        var cumulativeRevenue = 0m;
        var dailyData = dailyRaw.Select(d =>
        {
            cumulativeCount += d.Count;
            cumulativeRevenue += d.Revenue;
            return new DailyRegistrationPointDto
            {
                Date = d.Date,
                Count = d.Count,
                CumulativeCount = cumulativeCount,
                Revenue = d.Revenue,
                CumulativeRevenue = cumulativeRevenue,
            };
        }).ToList();

        return new RegistrationTimeSeriesDto
        {
            DailyData = dailyData,
            Summary = new RegistrationTrendSummaryDto
            {
                TotalRegistrations = summary?.Total ?? 0,
                TotalRevenue = summary?.TotalRevenue ?? 0m,
                TotalOutstanding = summary?.TotalOutstanding ?? 0m,
                PaidInFull = summary?.PaidInFull ?? 0,
                Underpaid = summary?.Underpaid ?? 0,
            }
        };
    }

    public async Task<AgegroupDistributionDto> GetAgegroupDistributionAsync(Guid jobId, CancellationToken ct = default)
    {
        // Player counts per age group (derived from assigned team)
        var playersByAg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true
                     && r.RoleId == RoleConstants.Player
                     && r.AssignedTeamId != null)
            .Join(_context.Teams.AsNoTracking(),
                  r => r.AssignedTeamId,
                  t => t.TeamId,
                  (r, t) => t.AgegroupId)
            .GroupBy(agId => agId)
            .Select(g => new { AgegroupId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Team counts per age group
        var teamsByAg = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && t.Active == true)
            .GroupBy(t => t.AgegroupId)
            .Select(g => new { AgegroupId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Agegroup name lookup for all referenced IDs
        var allAgIds = playersByAg.Select(p => p.AgegroupId)
            .Union(teamsByAg.Select(t => t.AgegroupId))
            .Distinct()
            .ToList();

        var agNames = await _context.Agegroups
            .AsNoTracking()
            .Where(ag => allAgIds.Contains(ag.AgegroupId))
            .Select(ag => new { ag.AgegroupId, ag.AgegroupName })
            .ToDictionaryAsync(ag => ag.AgegroupId, ag => ag.AgegroupName ?? "Unknown", ct);

        // Merge into distribution points
        var playerLookup = playersByAg.ToDictionary(p => p.AgegroupId, p => p.Count);
        var teamLookup = teamsByAg.ToDictionary(t => t.AgegroupId, t => t.Count);

        var points = allAgIds
            .Select(id => new AgegroupDistributionPointDto
            {
                AgegroupName = agNames.GetValueOrDefault(id, "Unknown"),
                PlayerCount = playerLookup.GetValueOrDefault(id, 0),
                TeamCount = teamLookup.GetValueOrDefault(id, 0),
            })
            .OrderBy(p => p.AgegroupName)
            .ToList();

        return new AgegroupDistributionDto
        {
            Agegroups = points,
            TotalPlayers = playersByAg.Sum(p => p.Count),
            TotalTeams = teamsByAg.Sum(t => t.Count),
        };
    }
}
