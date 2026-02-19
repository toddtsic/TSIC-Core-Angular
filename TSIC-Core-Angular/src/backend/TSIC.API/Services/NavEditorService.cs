using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services;

/// <summary>
/// Service for nav editor — manages platform defaults, job overrides,
/// and legacy menu import with route translation.
/// </summary>
public class NavEditorService : INavEditorService
{
    private readonly INavEditorRepository _navEditorRepo;

    /// <summary>
    /// Maps legacy Controller/Action paths to Angular RouterLink values.
    /// Matches the legacyRouteMap in client-menu.component.ts.
    /// </summary>
    private static readonly Dictionary<string, string> LegacyRouteMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scheduling/manageleagueseasonfields"] = "fields/index",
        ["scheduling/manageleagueseasonpairings"] = "pairings/index",
        ["scheduling/manageleagueseasontimeslots"] = "timeslots/index",
        ["scheduling/scheduledivbyagfields"] = "scheduling/scheduledivision",
        ["scheduling/getschedule"] = "scheduling/schedules",
    };

    public NavEditorService(INavEditorRepository navEditorRepo)
    {
        _navEditorRepo = navEditorRepo;
    }

    public async Task<List<NavEditorNavDto>> GetAllDefaultsAsync(CancellationToken ct = default)
    {
        return await _navEditorRepo.GetAllPlatformDefaultsAsync(ct);
    }

    public async Task<List<NavEditorLegacyMenuDto>> GetLegacyMenusAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _navEditorRepo.GetLegacyMenusForJobAsync(jobId, ct);
    }

    public async Task<NavEditorNavDto> CreateNavAsync(
        CreateNavRequest request, string userId, CancellationToken ct = default)
    {
        // Check if platform default already exists for this role
        if (request.JobId == null)
        {
            var exists = await _navEditorRepo.PlatformDefaultExistsAsync(request.RoleId, ct);
            if (exists)
                throw new InvalidOperationException($"Platform default already exists for role {request.RoleId}");
        }

        var now = DateTime.UtcNow;
        var nav = new Nav
        {
            RoleId = request.RoleId,
            JobId = request.JobId,
            Active = true,
            Modified = now,
            ModifiedBy = userId
        };

        _navEditorRepo.AddNav(nav);
        await _navEditorRepo.SaveChangesAsync(ct);

        return new NavEditorNavDto
        {
            NavId = nav.NavId,
            RoleId = nav.RoleId,
            RoleName = null, // Caller can reload to get role name
            JobId = nav.JobId,
            Active = nav.Active,
            IsDefault = nav.JobId == null,
            Items = new List<NavEditorNavItemDto>()
        };
    }

    public async Task<NavEditorNavItemDto> CreateNavItemAsync(
        CreateNavItemRequest request, string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Validate 2-level constraint
        if (request.ParentNavItemId != null)
        {
            var parent = await _navEditorRepo.GetNavItemByIdAsync(request.ParentNavItemId.Value, ct);
            if (parent == null)
                throw new InvalidOperationException($"Parent nav item {request.ParentNavItemId} not found");
            if (parent.ParentNavItemId != null)
                throw new InvalidOperationException("Cannot nest deeper than 2 levels");
        }

        var siblingCount = await _navEditorRepo.GetSiblingCountAsync(request.NavId, request.ParentNavItemId, ct);

        if (request.ParentNavItemId == null)
        {
            // Root item: create parent + auto-create stub child
            var parentItem = new NavItem
            {
                NavId = request.NavId,
                ParentNavItemId = null,
                Text = request.Text,
                IconName = request.IconName,
                RouterLink = request.RouterLink,
                NavigateUrl = request.NavigateUrl,
                Target = request.Target,
                Active = true,
                SortOrder = siblingCount + 1,
                Modified = now,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNavItem(parentItem);
            await _navEditorRepo.SaveChangesAsync(ct);

            // Auto-create stub child
            var stubChild = new NavItem
            {
                NavId = request.NavId,
                ParentNavItemId = parentItem.NavItemId,
                Text = "new child",
                Active = false,
                SortOrder = 1,
                Modified = now,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNavItem(stubChild);
            await _navEditorRepo.SaveChangesAsync(ct);

            return new NavEditorNavItemDto
            {
                NavItemId = parentItem.NavItemId,
                NavId = parentItem.NavId,
                ParentNavItemId = null,
                SortOrder = parentItem.SortOrder,
                Text = parentItem.Text,
                IconName = parentItem.IconName,
                RouterLink = parentItem.RouterLink,
                NavigateUrl = parentItem.NavigateUrl,
                Target = parentItem.Target,
                Active = parentItem.Active,
                Children = new List<NavEditorNavItemDto>
                {
                    new NavEditorNavItemDto
                    {
                        NavItemId = stubChild.NavItemId,
                        NavId = stubChild.NavId,
                        ParentNavItemId = stubChild.ParentNavItemId,
                        SortOrder = stubChild.SortOrder,
                        Text = stubChild.Text,
                        Active = stubChild.Active,
                        Children = new List<NavEditorNavItemDto>()
                    }
                }
            };
        }
        else
        {
            // Child item
            var child = new NavItem
            {
                NavId = request.NavId,
                ParentNavItemId = request.ParentNavItemId,
                Text = request.Text,
                IconName = request.IconName,
                RouterLink = request.RouterLink,
                NavigateUrl = request.NavigateUrl,
                Target = request.Target,
                Active = true,
                SortOrder = siblingCount + 1,
                Modified = now,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNavItem(child);
            await _navEditorRepo.SaveChangesAsync(ct);

            return new NavEditorNavItemDto
            {
                NavItemId = child.NavItemId,
                NavId = child.NavId,
                ParentNavItemId = child.ParentNavItemId,
                SortOrder = child.SortOrder,
                Text = child.Text,
                IconName = child.IconName,
                RouterLink = child.RouterLink,
                NavigateUrl = child.NavigateUrl,
                Target = child.Target,
                Active = child.Active,
                Children = new List<NavEditorNavItemDto>()
            };
        }
    }

    public async Task<NavEditorNavItemDto> UpdateNavItemAsync(
        int navItemId, UpdateNavItemRequest request, string userId, CancellationToken ct = default)
    {
        var item = await _navEditorRepo.GetNavItemByIdAsync(navItemId, ct);
        if (item == null)
            throw new InvalidOperationException($"Nav item {navItemId} not found");

        item.Text = request.Text;
        item.Active = request.Active;
        item.IconName = request.IconName;
        item.RouterLink = request.RouterLink;
        item.NavigateUrl = request.NavigateUrl;
        item.Target = request.Target;
        item.Modified = DateTime.UtcNow;
        item.ModifiedBy = userId;

        await _navEditorRepo.SaveChangesAsync(ct);

        return new NavEditorNavItemDto
        {
            NavItemId = item.NavItemId,
            NavId = item.NavId,
            ParentNavItemId = item.ParentNavItemId,
            SortOrder = item.SortOrder,
            Text = item.Text,
            IconName = item.IconName,
            RouterLink = item.RouterLink,
            NavigateUrl = item.NavigateUrl,
            Target = item.Target,
            Active = item.Active,
            Children = new List<NavEditorNavItemDto>()
        };
    }

    public async Task DeleteNavItemAsync(int navItemId, CancellationToken ct = default)
    {
        var item = await _navEditorRepo.GetNavItemByIdAsync(navItemId, ct);
        if (item == null)
            throw new InvalidOperationException($"Nav item {navItemId} not found");

        var siblingCount = await _navEditorRepo.GetSiblingCountAsync(item.NavId, item.ParentNavItemId, ct);

        if (siblingCount > 1)
        {
            _navEditorRepo.RemoveNavItem(item);
        }
        else
        {
            // Soft delete — last sibling, prevent orphaning parent
            item.Active = false;
            item.Modified = DateTime.UtcNow;
        }

        await _navEditorRepo.SaveChangesAsync(ct);
    }

    public async Task ReorderNavItemsAsync(
        ReorderNavItemsRequest request, string userId, CancellationToken ct = default)
    {
        var siblings = await _navEditorRepo.GetSiblingItemsAsync(request.NavId, request.ParentNavItemId, ct);
        var now = DateTime.UtcNow;

        for (int i = 0; i < request.OrderedItemIds.Count; i++)
        {
            var item = siblings.FirstOrDefault(s => s.NavItemId == request.OrderedItemIds[i]);
            if (item != null)
            {
                item.SortOrder = i + 1;
                item.Modified = now;
                item.ModifiedBy = userId;
            }
        }

        await _navEditorRepo.SaveChangesAsync(ct);
    }

    public async Task<NavEditorNavDto> ImportLegacyMenuAsync(
        ImportLegacyMenuRequest request, string userId, CancellationToken ct = default)
    {
        // Get legacy menu items
        var legacyMenus = await _navEditorRepo.GetLegacyMenusForJobAsync(Guid.Empty, ct);
        var sourceMenu = legacyMenus.FirstOrDefault(m => m.MenuId == request.SourceMenuId);
        if (sourceMenu == null)
            throw new InvalidOperationException($"Legacy menu {request.SourceMenuId} not found");

        // Ensure platform default nav exists for this role
        var defaultExists = await _navEditorRepo.PlatformDefaultExistsAsync(request.TargetRoleId, ct);
        Nav? nav;

        if (!defaultExists)
        {
            nav = new Nav
            {
                RoleId = request.TargetRoleId,
                JobId = null,
                Active = true,
                Modified = DateTime.UtcNow,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNav(nav);
            await _navEditorRepo.SaveChangesAsync(ct);
        }
        else
        {
            // Find existing default nav
            var defaults = await _navEditorRepo.GetAllPlatformDefaultsAsync(ct);
            var existing = defaults.FirstOrDefault(d => d.RoleId == request.TargetRoleId);
            nav = existing != null
                ? await _navEditorRepo.GetNavByIdAsync(existing.NavId, ct)
                : null;

            if (nav == null)
                throw new InvalidOperationException($"Could not find platform default for role {request.TargetRoleId}");
        }

        // Get current max sort order
        var existingCount = await _navEditorRepo.GetSiblingCountAsync(nav.NavId, null, ct);
        var now = DateTime.UtcNow;
        var sortOrder = existingCount;

        // Import root items and their children
        foreach (var legacyRoot in sourceMenu.Items.Where(i => i.Active))
        {
            sortOrder++;
            var parentItem = new NavItem
            {
                NavId = nav.NavId,
                ParentNavItemId = null,
                Text = legacyRoot.Text ?? "Untitled",
                IconName = legacyRoot.IconName,
                RouterLink = TranslateLegacyRoute(legacyRoot),
                NavigateUrl = legacyRoot.NavigateUrl,
                Target = legacyRoot.Target,
                Active = true,
                SortOrder = sortOrder,
                Modified = now,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNavItem(parentItem);
            await _navEditorRepo.SaveChangesAsync(ct);

            // Import children
            var childSort = 0;
            foreach (var legacyChild in legacyRoot.Children.Where(c => c.Active))
            {
                childSort++;
                var childItem = new NavItem
                {
                    NavId = nav.NavId,
                    ParentNavItemId = parentItem.NavItemId,
                    Text = legacyChild.Text ?? "Untitled",
                    IconName = legacyChild.IconName,
                    RouterLink = TranslateLegacyRoute(legacyChild),
                    NavigateUrl = legacyChild.NavigateUrl,
                    Target = legacyChild.Target,
                    Active = true,
                    SortOrder = childSort,
                    Modified = now,
                    ModifiedBy = userId
                };
                _navEditorRepo.AddNavItem(childItem);
            }

            await _navEditorRepo.SaveChangesAsync(ct);
        }

        // Reload and return the updated nav
        var allDefaults = await _navEditorRepo.GetAllPlatformDefaultsAsync(ct);
        return allDefaults.First(d => d.NavId == nav.NavId);
    }

    public async Task ToggleNavActiveAsync(int navId, bool active, CancellationToken ct = default)
    {
        var nav = await _navEditorRepo.GetNavByIdAsync(navId, ct);
        if (nav == null)
            throw new InvalidOperationException($"Nav {navId} not found");

        nav.Active = active;
        nav.Modified = DateTime.UtcNow;
        await _navEditorRepo.SaveChangesAsync(ct);
    }

    public async Task<int> EnsureAllRoleNavsAsync(string userId, CancellationToken ct = default)
    {
        // All roles that should have platform default navs
        var standardRoleIds = new[]
        {
            RoleConstants.Director,
            RoleConstants.SuperDirector,
            RoleConstants.Superuser,
            RoleConstants.Family,
            RoleConstants.Player,
            RoleConstants.ClubRep,
            RoleConstants.RefAssignor,
            RoleConstants.Staff,
            RoleConstants.StoreAdmin,
            RoleConstants.StpAdmin,
        };

        var now = DateTime.UtcNow;
        var created = 0;

        foreach (var roleId in standardRoleIds)
        {
            var exists = await _navEditorRepo.PlatformDefaultExistsAsync(roleId, ct);
            if (!exists)
            {
                var nav = new Nav
                {
                    RoleId = roleId,
                    JobId = null,
                    Active = true,
                    Modified = now,
                    ModifiedBy = userId
                };
                _navEditorRepo.AddNav(nav);
                await _navEditorRepo.SaveChangesAsync(ct);
                created++;
            }
        }

        return created;
    }

    // ─── Private helpers ────────────────────────────────────────────

    /// <summary>
    /// Translates a legacy menu item's Controller/Action to a RouterLink.
    /// If RouterLink is already set, returns it directly.
    /// </summary>
    private static string? TranslateLegacyRoute(NavEditorLegacyItemDto item)
    {
        // Prefer existing RouterLink
        if (!string.IsNullOrWhiteSpace(item.RouterLink))
            return item.RouterLink;

        // Try to translate Controller/Action
        if (!string.IsNullOrWhiteSpace(item.Controller) && !string.IsNullOrWhiteSpace(item.Action))
        {
            var key = $"{item.Controller}/{item.Action}";
            if (LegacyRouteMap.TryGetValue(key, out var mapped))
                return mapped;

            // Return controller/action as-is for manual review
            return key.ToLowerInvariant();
        }

        return null;
    }
}
