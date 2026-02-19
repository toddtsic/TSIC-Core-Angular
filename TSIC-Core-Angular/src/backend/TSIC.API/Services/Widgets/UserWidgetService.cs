using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Widgets;

/// <summary>
/// Service for managing per-user widget customizations.
/// </summary>
public sealed class UserWidgetService : IUserWidgetService
{
    private readonly IUserWidgetRepository _userWidgetRepo;
    private readonly IWidgetRepository _widgetRepo;

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

    public UserWidgetService(
        IUserWidgetRepository userWidgetRepo,
        IWidgetRepository widgetRepo)
    {
        _userWidgetRepo = userWidgetRepo;
        _widgetRepo = widgetRepo;
    }

    public async Task<List<UserWidgetEntryDto>> GetUserWidgetsAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        var entities = await _userWidgetRepo.GetByRegistrationIdAsync(registrationId, ct);

        return entities
            .Select(uw => new UserWidgetEntryDto
            {
                WidgetId = uw.WidgetId,
                CategoryId = uw.CategoryId,
                DisplayOrder = uw.DisplayOrder,
                IsHidden = uw.IsHidden,
                Config = uw.Config
            })
            .ToList();
    }

    public async Task SaveUserWidgetsAsync(
        Guid registrationId, SaveUserWidgetsRequest request, CancellationToken ct = default)
    {
        // Replace-all: delete existing, then insert new
        var existing = await _userWidgetRepo.GetByRegistrationIdAsync(registrationId, ct);
        if (existing.Count > 0)
        {
            _userWidgetRepo.RemoveRange(existing);
        }

        var newEntities = request.Entries.Select(entry => new UserWidget
        {
            RegistrationId = registrationId,
            WidgetId = entry.WidgetId,
            CategoryId = entry.CategoryId,
            DisplayOrder = entry.DisplayOrder,
            IsHidden = entry.IsHidden,
            Config = entry.Config
        });

        await _userWidgetRepo.AddRangeAsync(newEntities, ct);
        await _userWidgetRepo.SaveChangesAsync(ct);
    }

    public async Task ResetUserWidgetsAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        var existing = await _userWidgetRepo.GetByRegistrationIdAsync(registrationId, ct);
        if (existing.Count > 0)
        {
            _userWidgetRepo.RemoveRange(existing);
            await _userWidgetRepo.SaveChangesAsync(ct);
        }
    }

    public async Task<List<AvailableWidgetDto>> GetAvailableWidgetsAsync(
        Guid jobId, string roleName, Guid registrationId, CancellationToken ct = default)
    {
        if (!RoleNameToIdMap.TryGetValue(roleName, out var roleId))
            return [];

        var jobTypeId = await _widgetRepo.GetJobTypeIdAsync(jobId, ct);
        if (jobTypeId is null)
            return [];

        // Get all widgets available for this role+jobType (Layer 1)
        var defaults = await _widgetRepo.GetDefaultsAsync(jobTypeId.Value, roleId, ct);

        // Get job overrides (Layer 2) to determine final enabled set
        var jobWidgets = await _widgetRepo.GetJobWidgetsAsync(jobId, roleId, ct);
        var jobOverrides = jobWidgets.ToDictionary(jw => jw.WidgetId);

        // Get user customizations (Layer 3) to determine visibility
        var userWidgets = await _userWidgetRepo.GetByRegistrationIdAsync(registrationId, ct);
        var userOverrides = userWidgets.ToDictionary(uw => uw.WidgetId);

        var available = new List<AvailableWidgetDto>();

        foreach (var def in defaults)
        {
            // Skip widgets disabled at job level
            if (jobOverrides.TryGetValue(def.WidgetId, out var jw) && !jw.IsEnabled)
                continue;

            var isHidden = userOverrides.TryGetValue(def.WidgetId, out var uw) && uw.IsHidden;

            available.Add(new AvailableWidgetDto
            {
                WidgetId = def.WidgetId,
                Name = def.WidgetName,
                WidgetType = def.WidgetType,
                ComponentKey = def.ComponentKey,
                Description = def.Description,
                CategoryId = def.CategoryId,
                CategoryName = def.CategoryName,
                Workspace = def.Workspace,
                IsVisible = !isHidden
            });
        }

        // Include job-specific additions not in defaults
        foreach (var addition in jobOverrides.Values)
        {
            if (!addition.IsEnabled) continue;
            if (defaults.Any(d => d.WidgetId == addition.WidgetId)) continue;

            var isHidden = userOverrides.TryGetValue(addition.WidgetId, out var uw) && uw.IsHidden;

            available.Add(new AvailableWidgetDto
            {
                WidgetId = addition.WidgetId,
                Name = addition.WidgetName,
                WidgetType = addition.WidgetType,
                ComponentKey = addition.ComponentKey,
                Description = addition.Description,
                CategoryId = addition.CategoryId,
                CategoryName = addition.CategoryName,
                Workspace = addition.Workspace,
                IsVisible = !isHidden
            });
        }

        return available;
    }
}
