using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Widgets;

/// <summary>
/// Assembles the widget dashboard by merging WidgetDefault (Role+JobType)
/// with JobWidget (per-job overrides) and grouping into sections/categories.
/// </summary>
public sealed class WidgetDashboardService : IWidgetDashboardService
{
    private readonly IWidgetRepository _widgetRepo;
    private readonly ISchedulingDashboardService _schedulingSvc;
    private readonly ILogger<WidgetDashboardService> _logger;

    // Section ordering: content first, then health, action, insight
    private static readonly Dictionary<string, int> SectionOrder = new()
    {
        ["content"] = -1,
        ["health"] = 0,
        ["action"] = 1,
        ["insight"] = 2
    };

    /// <summary>
    /// Maps JWT role name claims to AspNetRoles.Id GUIDs.
    /// </summary>
    private static readonly Dictionary<string, string> RoleNameToIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Director"] = RoleConstants.Director,
        ["SuperDirector"] = RoleConstants.SuperDirector,
        ["Superuser"] = RoleConstants.Superuser,
        ["Club Rep"] = RoleConstants.ClubRep,
        ["Family"] = RoleConstants.Family,
        ["Player"] = RoleConstants.Player,
        ["Ref Assignor"] = RoleConstants.RefAssignor,
        ["Referee"] = RoleConstants.Referee,
        ["Scorer"] = RoleConstants.Scorer,
        ["Staff"] = RoleConstants.Staff,
        ["Store Admin"] = RoleConstants.StoreAdmin,
        ["STPAdmin"] = RoleConstants.StpAdmin,
        ["Recruiter"] = RoleConstants.Recruiter,
        ["Guest"] = RoleConstants.Guest,
        ["Anonymous"] = RoleConstants.Anonymous,
    };

    public WidgetDashboardService(
        IWidgetRepository widgetRepo,
        ISchedulingDashboardService schedulingSvc,
        ILogger<WidgetDashboardService> logger)
    {
        _widgetRepo = widgetRepo;
        _schedulingSvc = schedulingSvc;
        _logger = logger;
    }

    public async Task<WidgetDashboardResponse> GetDashboardAsync(
        Guid jobId, string roleName, CancellationToken ct = default)
    {
        // 1. Resolve role name to GUID
        if (!RoleNameToIdMap.TryGetValue(roleName, out var roleId))
        {
            _logger.LogWarning("Widget dashboard: unknown role name '{RoleName}'", roleName);
            return new WidgetDashboardResponse { Sections = [] };
        }

        // 2. Resolve job type
        var jobTypeId = await _widgetRepo.GetJobTypeIdAsync(jobId, ct);
        if (jobTypeId is null)
        {
            _logger.LogWarning("Widget dashboard requested for unknown job {JobId}", jobId);
            return new WidgetDashboardResponse { Sections = [] };
        }

        // 2. Fetch defaults and per-job overrides (sequential — DbContext is not thread-safe)
        var defaults = await _widgetRepo.GetDefaultsAsync(jobTypeId.Value, roleId, ct);
        var jobWidgets = await _widgetRepo.GetJobWidgetsAsync(jobId, roleId, ct);

        // 3. Build lookup of per-job overrides keyed by WidgetId
        var overridesByWidgetId = jobWidgets.ToDictionary(jw => jw.WidgetId);

        // 4. Merge: start with defaults, apply overrides
        var mergedWidgets = new List<MergedWidget>();

        foreach (var def in defaults)
        {
            if (overridesByWidgetId.TryGetValue(def.WidgetId, out var ov))
            {
                // Override exists
                if (!ov.IsEnabled)
                    continue; // Explicitly disabled for this job

                mergedWidgets.Add(new MergedWidget
                {
                    WidgetId = def.WidgetId,
                    Name = def.WidgetName,
                    WidgetType = def.WidgetType,
                    ComponentKey = def.ComponentKey,
                    Description = def.Description,
                    CategoryId = ov.CategoryId,
                    CategoryName = ov.CategoryName,
                    CategoryIcon = ov.CategoryIcon,
                    CategoryDefaultOrder = ov.CategoryDefaultOrder,
                    Section = ov.Section,
                    DisplayOrder = ov.DisplayOrder,
                    Config = ov.Config ?? def.Config,
                    IsOverridden = true
                });

                overridesByWidgetId.Remove(def.WidgetId);
            }
            else
            {
                // No override — use default as-is
                mergedWidgets.Add(new MergedWidget
                {
                    WidgetId = def.WidgetId,
                    Name = def.WidgetName,
                    WidgetType = def.WidgetType,
                    ComponentKey = def.ComponentKey,
                    Description = def.Description,
                    CategoryId = def.CategoryId,
                    CategoryName = def.CategoryName,
                    CategoryIcon = def.CategoryIcon,
                    CategoryDefaultOrder = def.CategoryDefaultOrder,
                    Section = def.Section,
                    DisplayOrder = def.DisplayOrder,
                    Config = def.Config,
                    IsOverridden = false
                });
            }
        }

        // 5. Add job-specific widgets that have no default (pure additions)
        foreach (var addition in overridesByWidgetId.Values)
        {
            if (!addition.IsEnabled)
                continue;

            mergedWidgets.Add(new MergedWidget
            {
                WidgetId = addition.WidgetId,
                Name = addition.WidgetName,
                WidgetType = addition.WidgetType,
                ComponentKey = addition.ComponentKey,
                Description = addition.Description,
                CategoryId = addition.CategoryId,
                CategoryName = addition.CategoryName,
                CategoryIcon = addition.CategoryIcon,
                CategoryDefaultOrder = addition.CategoryDefaultOrder,
                Section = addition.Section,
                DisplayOrder = addition.DisplayOrder,
                Config = addition.Config,
                IsOverridden = true
            });
        }

        // 6. Group into sections → categories → ordered widgets
        var sections = mergedWidgets
            .GroupBy(w => w.Section)
            .OrderBy(g => SectionOrder.GetValueOrDefault(g.Key, 99))
            .Select(sectionGroup => new WidgetSectionDto
            {
                Section = sectionGroup.Key,
                Categories = sectionGroup
                    .GroupBy(w => w.CategoryId)
                    .OrderBy(g => g.First().CategoryDefaultOrder)
                    .Select(catGroup => new WidgetCategoryGroupDto
                    {
                        CategoryId = catGroup.Key,
                        CategoryName = catGroup.First().CategoryName,
                        Icon = catGroup.First().CategoryIcon,
                        DisplayOrder = catGroup.First().CategoryDefaultOrder,
                        Widgets = catGroup
                            .OrderBy(w => w.DisplayOrder)
                            .Select(w => new WidgetItemDto
                            {
                                WidgetId = w.WidgetId,
                                Name = w.Name,
                                WidgetType = w.WidgetType,
                                ComponentKey = w.ComponentKey,
                                DisplayOrder = w.DisplayOrder,
                                Config = w.Config,
                                Description = w.Description,
                                IsOverridden = w.IsOverridden
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();

        return new WidgetDashboardResponse { Sections = sections };
    }

    public async Task<DashboardMetricsDto> GetMetricsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Get registration + financial metrics from repository
        var metrics = await _widgetRepo.GetDashboardMetricsAsync(jobId, ct);

        // Get scheduling metrics — separate service (uses different repos, same DbContext scope)
        try
        {
            var schedStatus = await _schedulingSvc.GetStatusAsync(jobId, ct);
            metrics = metrics with
            {
                Scheduling = new SchedulingMetrics
                {
                    TotalAgegroups = schedStatus.TotalAgegroups,
                    AgegroupsScheduled = schedStatus.AgegroupsScheduled,
                    FieldCount = schedStatus.FieldCount,
                    TotalDivisions = schedStatus.TotalDivisions,
                }
            };
        }
        catch (Exception ex)
        {
            // Scheduling data is optional — log and return zeros
            _logger.LogWarning(ex, "Failed to load scheduling metrics for job {JobId}", jobId);
        }

        return metrics;
    }

    /// <summary>
    /// Internal struct for holding merged widget data before grouping.
    /// </summary>
    private sealed class MergedWidget
    {
        public int WidgetId { get; init; }
        public string Name { get; init; } = "";
        public string WidgetType { get; init; } = "";
        public string ComponentKey { get; init; } = "";
        public string? Description { get; init; }
        public int CategoryId { get; init; }
        public string CategoryName { get; init; } = "";
        public string? CategoryIcon { get; init; }
        public int CategoryDefaultOrder { get; init; }
        public string Section { get; init; } = "";
        public int DisplayOrder { get; init; }
        public string? Config { get; init; }
        public bool IsOverridden { get; init; }
    }
}
