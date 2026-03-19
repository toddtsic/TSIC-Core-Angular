using System.Text;
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
        ["scheduling/manageleagueseasonfields"] = "scheduling/fields",
        ["scheduling/manageleagueseasonpairings"] = "scheduling/pairings",
        ["scheduling/manageleagueseasontimeslots"] = "scheduling/timeslots",
        ["scheduling/scheduledivbyagfields"] = "scheduling/schedule-hub",
        ["scheduling/getschedule"] = "scheduling/view-schedule",
        ["search/changepassword"] = "tools/change-password",
        ["customerjobrevenue/index"] = "tools/customer-job-revenue",
        ["bracketseeds/index"] = "scheduling/bracket-seeds",
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

        // Hide rows are root-level override items with DefaultNavItemId set — skip 2-level and stub logic
        if (request.DefaultNavItemId != null)
        {
            var hideRow = new NavItem
            {
                NavId = request.NavId,
                ParentNavItemId = null,
                DefaultNavItemId = request.DefaultNavItemId,
                DefaultParentNavItemId = null,
                Text = null,
                Active = false, // Active=false marks this as a hide row
                SortOrder = 0,
                Modified = now,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNavItem(hideRow);
            await _navEditorRepo.SaveChangesAsync(ct);

            return new NavEditorNavItemDto
            {
                NavItemId = hideRow.NavItemId,
                NavId = hideRow.NavId,
                ParentNavItemId = null,
                SortOrder = 0,
                Text = null,
                Active = false,
                DefaultNavItemId = hideRow.DefaultNavItemId,
                DefaultParentNavItemId = null,
                VisibilityRules = null,
                Children = new List<NavEditorNavItemDto>()
            };
        }

        // Validate 2-level constraint for normal items
        if (request.ParentNavItemId != null)
        {
            var parent = await _navEditorRepo.GetNavItemByIdAsync(request.ParentNavItemId.Value, ct);
            if (parent == null)
                throw new InvalidOperationException($"Parent nav item {request.ParentNavItemId} not found");
            if (parent.ParentNavItemId != null)
                throw new InvalidOperationException("Cannot nest deeper than 2 levels");
        }

        var siblingCount = await _navEditorRepo.GetSiblingCountAsync(request.NavId, request.ParentNavItemId, ct);

        // Items slotted under a default parent (DefaultParentNavItemId set) are always children — no stub
        if (request.DefaultParentNavItemId != null || request.ParentNavItemId != null)
        {
            var child = new NavItem
            {
                NavId = request.NavId,
                ParentNavItemId = request.ParentNavItemId,
                DefaultNavItemId = null,
                DefaultParentNavItemId = request.DefaultParentNavItemId,
                Text = request.Text,
                IconName = request.IconName,
                RouterLink = request.RouterLink,
                NavigateUrl = request.NavigateUrl,
                Target = request.Target,
                VisibilityRules = request.VisibilityRules,
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
                DefaultNavItemId = null,
                DefaultParentNavItemId = child.DefaultParentNavItemId,
                VisibilityRules = child.VisibilityRules,
                Children = new List<NavEditorNavItemDto>()
            };
        }

        // Root item: create parent + auto-create stub child
        var parentItem = new NavItem
        {
            NavId = request.NavId,
            ParentNavItemId = null,
            DefaultNavItemId = null,
            DefaultParentNavItemId = null,
            Text = request.Text,
            IconName = request.IconName,
            RouterLink = request.RouterLink,
            NavigateUrl = request.NavigateUrl,
            Target = request.Target,
            VisibilityRules = request.VisibilityRules,
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
            DefaultNavItemId = null,
            DefaultParentNavItemId = null,
            VisibilityRules = parentItem.VisibilityRules,
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
        item.VisibilityRules = request.VisibilityRules;
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
            VisibilityRules = item.VisibilityRules,
            Children = new List<NavEditorNavItemDto>()
        };
    }

    public async Task<int> CascadeRouteAsync(CascadeRouteRequest request, string userId, CancellationToken ct = default)
    {
        var matches = await _navEditorRepo.GetMatchingItemsAcrossDefaultNavsAsync(request.NavItemId, ct);

        var now = DateTime.UtcNow;
        foreach (var match in matches)
        {
            match.RouterLink = request.RouterLink;
            match.NavigateUrl = request.NavigateUrl;
            match.Target = request.Target;
            match.Modified = now;
            match.ModifiedBy = userId;
        }

        await _navEditorRepo.SaveChangesAsync(ct);
        return matches.Count;
    }

    public async Task<DeleteNavItemResult?> DeleteNavItemAsync(int navItemId, bool force = false, CancellationToken ct = default)
    {
        var item = await _navEditorRepo.GetNavItemByIdAsync(navItemId, ct);
        if (item == null)
            throw new InvalidOperationException($"Nav item {navItemId} not found");

        // Check if any job override items reference this item
        var references = await _navEditorRepo.GetReferencingOverrideItemsAsync(navItemId, ct);
        if (references.Count > 0 && !force)
        {
            return new DeleteNavItemResult
            {
                RequiresConfirmation = true,
                Message = $"{references.Count} job override item(s) reference this item. Deleting will also remove those overrides.",
                AffectedCount = references.Count
            };
        }

        // Remove override references first (force=true or already handled above)
        if (references.Count > 0)
            _navEditorRepo.RemoveNavItems(references);

        // If this is a parent item, remove its direct children too
        if (item.ParentNavItemId == null)
        {
            var children = await _navEditorRepo.GetSiblingItemsAsync(item.NavId, item.NavItemId, ct);
            if (children.Count > 0)
                _navEditorRepo.RemoveNavItems(children);
        }

        _navEditorRepo.RemoveNavItem(item);
        await _navEditorRepo.SaveChangesAsync(ct);
        return null;
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

    public async Task MoveNavItemAsync(
        int navItemId, MoveNavItemRequest request, string userId, CancellationToken ct = default)
    {
        // 1. Load and validate source item — must be Level 2
        var item = await _navEditorRepo.GetNavItemByIdAsync(navItemId, ct);
        if (item == null)
            throw new InvalidOperationException($"Nav item {navItemId} not found");
        if (item.ParentNavItemId == null)
            throw new InvalidOperationException("Only child (Level 2) items can be moved between groups");

        // 2. Load and validate target parent — must be Level 1
        var targetParent = await _navEditorRepo.GetNavItemByIdAsync(request.TargetParentNavItemId, ct);
        if (targetParent == null)
            throw new InvalidOperationException($"Target parent {request.TargetParentNavItemId} not found");
        if (targetParent.ParentNavItemId != null)
            throw new InvalidOperationException("Target must be a Level 1 (root) item");

        // 3. Must belong to same nav
        if (item.NavId != targetParent.NavId)
            throw new InvalidOperationException("Source and target must belong to the same nav");

        // 4. Must be a different parent
        if (item.ParentNavItemId == targetParent.NavItemId)
            throw new InvalidOperationException("Item is already under this parent");

        var oldParentId = item.ParentNavItemId.Value;
        var now = DateTime.UtcNow;

        // 5. Append to end of target group
        var targetSiblingCount = await _navEditorRepo.GetSiblingCountAsync(item.NavId, targetParent.NavItemId, ct);
        item.ParentNavItemId = targetParent.NavItemId;
        item.SortOrder = targetSiblingCount + 1;
        item.Modified = now;
        item.ModifiedBy = userId;

        // 6. Reindex source group to fill the gap
        var sourceSiblings = await _navEditorRepo.GetSiblingItemsAsync(item.NavId, oldParentId, ct);
        var sorted = sourceSiblings
            .Where(s => s.NavItemId != navItemId)
            .OrderBy(s => s.SortOrder)
            .ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].SortOrder = i + 1;
            sorted[i].Modified = now;
            sorted[i].ModifiedBy = userId;
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

    public async Task<int> CloneBranchAsync(
        CloneBranchRequest request, string userId, CancellationToken ct = default)
    {
        // 1. Load and validate source item
        var sourceItem = await _navEditorRepo.GetNavItemByIdAsync(request.SourceNavItemId, ct);
        if (sourceItem == null)
            throw new InvalidOperationException($"Source nav item {request.SourceNavItemId} not found");
        if (sourceItem.ParentNavItemId != null)
            throw new InvalidOperationException("Source item must be a Level 1 (root) item");

        // 2. Validate target nav exists and differs from source
        var targetNav = await _navEditorRepo.GetNavByIdAsync(request.TargetNavId, ct);
        if (targetNav == null)
            throw new InvalidOperationException($"Target nav {request.TargetNavId} not found");
        if (targetNav.NavId == sourceItem.NavId)
            throw new InvalidOperationException("Cannot clone to the same nav");

        // 3. Load source's active children
        var allSourceItems = await _navEditorRepo.GetNavItemsByNavIdAsync(sourceItem.NavId, ct);
        var activeChildren = allSourceItems
            .Where(i => i.ParentNavItemId == sourceItem.NavItemId && i.Active)
            .OrderBy(i => i.SortOrder)
            .ToList();

        // 4. If ReplaceExisting, find and remove duplicate in target
        if (request.ReplaceExisting)
        {
            var targetItems = await _navEditorRepo.GetNavItemsByNavIdAsync(request.TargetNavId, ct);
            var duplicate = targetItems.FirstOrDefault(
                i => i.ParentNavItemId == null
                  && string.Equals(i.Text, sourceItem.Text, StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                var trackedChildren = await _navEditorRepo.GetSiblingItemsAsync(
                    request.TargetNavId, duplicate.NavItemId, ct);
                var trackedDuplicate = await _navEditorRepo.GetNavItemByIdAsync(duplicate.NavItemId, ct);

                if (trackedChildren.Count > 0)
                    _navEditorRepo.RemoveNavItems(trackedChildren);
                if (trackedDuplicate != null)
                    _navEditorRepo.RemoveNavItem(trackedDuplicate);

                await _navEditorRepo.SaveChangesAsync(ct);
            }
        }

        // 5. Determine sort order for new parent
        var siblingCount = await _navEditorRepo.GetSiblingCountAsync(request.TargetNavId, null, ct);
        var now = DateTime.UtcNow;

        // 6. Create the cloned parent
        var clonedParent = new NavItem
        {
            NavId = request.TargetNavId,
            ParentNavItemId = null,
            Text = sourceItem.Text,
            IconName = sourceItem.IconName,
            RouterLink = sourceItem.RouterLink,
            NavigateUrl = sourceItem.NavigateUrl,
            Target = sourceItem.Target,
            VisibilityRules = sourceItem.VisibilityRules,
            Active = true,
            SortOrder = siblingCount + 1,
            Modified = now,
            ModifiedBy = userId
        };
        _navEditorRepo.AddNavItem(clonedParent);
        await _navEditorRepo.SaveChangesAsync(ct);

        // 7. Create cloned children
        var childSort = 0;
        foreach (var sourceChild in activeChildren)
        {
            childSort++;
            var clonedChild = new NavItem
            {
                NavId = request.TargetNavId,
                ParentNavItemId = clonedParent.NavItemId,
                Text = sourceChild.Text,
                IconName = sourceChild.IconName,
                RouterLink = sourceChild.RouterLink,
                NavigateUrl = sourceChild.NavigateUrl,
                Target = sourceChild.Target,
                VisibilityRules = sourceChild.VisibilityRules,
                Active = true,
                SortOrder = childSort,
                Modified = now,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNavItem(clonedChild);
        }

        await _navEditorRepo.SaveChangesAsync(ct);

        // Return total items cloned (parent + children)
        return 1 + childSort;
    }

    public async Task<List<NavEditorNavDto>> GetJobOverridesAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _navEditorRepo.GetJobOverridesAsync(jobId, ct);
    }

    public async Task<int> EnsureJobOverrideNavAsync(
        Guid jobId, string roleId, string userId, CancellationToken ct = default)
    {
        var overrideNav = await _navEditorRepo.GetJobOverrideNavAsync(jobId, roleId, ct);
        if (overrideNav != null)
            return overrideNav.NavId;

        overrideNav = new Nav
        {
            RoleId = roleId,
            JobId = jobId,
            Active = true,
            Modified = DateTime.UtcNow,
            ModifiedBy = userId
        };
        _navEditorRepo.AddNav(overrideNav);
        await _navEditorRepo.SaveChangesAsync(ct);
        return overrideNav.NavId;
    }

    public async Task ToggleHideAsync(
        Guid jobId, string roleId, int defaultNavItemId, bool hide, string userId, CancellationToken ct = default)
    {
        // Ensure the job override nav exists for this role
        var overrideNav = await _navEditorRepo.GetJobOverrideNavAsync(jobId, roleId, ct);

        if (overrideNav == null)
        {
            // Create on demand
            overrideNav = new Nav
            {
                RoleId = roleId,
                JobId = jobId,
                Active = true,
                Modified = DateTime.UtcNow,
                ModifiedBy = userId
            };
            _navEditorRepo.AddNav(overrideNav);
            await _navEditorRepo.SaveChangesAsync(ct);
        }

        if (hide)
        {
            // Create hide row if it doesn't already exist
            var existing = await _navEditorRepo.GetHideRowAsync(overrideNav.NavId, defaultNavItemId, ct);
            if (existing == null)
            {
                var hideRow = new NavItem
                {
                    NavId = overrideNav.NavId,
                    ParentNavItemId = null,
                    DefaultNavItemId = defaultNavItemId,
                    DefaultParentNavItemId = null,
                    Text = null,
                    Active = false,
                    SortOrder = 0,
                    Modified = DateTime.UtcNow,
                    ModifiedBy = userId
                };
                _navEditorRepo.AddNavItem(hideRow);
                await _navEditorRepo.SaveChangesAsync(ct);
            }
        }
        else
        {
            // Remove hide row if it exists
            var existing = await _navEditorRepo.GetHideRowAsync(overrideNav.NavId, defaultNavItemId, ct);
            if (existing != null)
            {
                _navEditorRepo.RemoveNavItem(existing);
                await _navEditorRepo.SaveChangesAsync(ct);
            }
        }
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

    public async Task<string> ExportNavSqlAsync(CancellationToken ct = default)
    {
        var navs = await _navEditorRepo.GetAllPlatformDefaultsAsync(ct);
        var sb = new StringBuilder();

        sb.AppendLine("-- ============================================================================");
        sb.AppendLine("-- Nav Platform Defaults — Export Script");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("-- Idempotent: creates schema/tables if needed, clears and reseeds data.");
        sb.AppendLine("-- Target: naive production system (no prior nav schema required).");
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine();

        // ── 1. Create schema ──
        sb.AppendLine("-- ── 1. Create [nav] schema if not exists ──");
        sb.AppendLine();
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'nav')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    EXEC('CREATE SCHEMA [nav] AUTHORIZATION [dbo]');");
        sb.AppendLine("    PRINT 'Created [nav] schema';");
        sb.AppendLine("END");
        sb.AppendLine();

        // ── 2. Create tables ──
        sb.AppendLine("-- ── 2. Create tables if not exists ──");
        sb.AppendLine();
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'Nav')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    CREATE TABLE [nav].[Nav]");
        sb.AppendLine("    (");
        sb.AppendLine("        [NavId]         INT IDENTITY(1,1)       NOT NULL,");
        sb.AppendLine("        [RoleId]        NVARCHAR(450)           NOT NULL,");
        sb.AppendLine("        [JobId]         UNIQUEIDENTIFIER        NULL,");
        sb.AppendLine("        [Active]        BIT                     NOT NULL    DEFAULT 1,");
        sb.AppendLine("        [Modified]      DATETIME2               NOT NULL    DEFAULT GETDATE(),");
        sb.AppendLine("        [ModifiedBy]    NVARCHAR(450)           NULL,");
        sb.AppendLine();
        sb.AppendLine("        CONSTRAINT [PK_nav_Nav] PRIMARY KEY CLUSTERED ([NavId]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_Nav_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_Nav_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_Nav_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])");
        sb.AppendLine("    );");
        sb.AppendLine();
        sb.AppendLine("    CREATE UNIQUE INDEX [UQ_nav_Nav_Role_Job] ON [nav].[Nav] ([RoleId], [JobId]) WHERE [JobId] IS NOT NULL;");
        sb.AppendLine("    PRINT 'Created table: nav.Nav';");
        sb.AppendLine("END");
        sb.AppendLine();
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'nav' AND t.name = 'NavItem')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    CREATE TABLE [nav].[NavItem]");
        sb.AppendLine("    (");
        sb.AppendLine("        [NavItemId]              INT IDENTITY(1,1)   NOT NULL,");
        sb.AppendLine("        [NavId]                  INT                 NOT NULL,");
        sb.AppendLine("        [ParentNavItemId]        INT                 NULL,");
        sb.AppendLine("        [DefaultNavItemId]       INT                 NULL,");
        sb.AppendLine("        [DefaultParentNavItemId] INT                 NULL,");
        sb.AppendLine("        [Active]                 BIT                 NOT NULL    DEFAULT 1,");
        sb.AppendLine("        [SortOrder]              INT                 NOT NULL    DEFAULT 0,");
        sb.AppendLine("        [Text]                   NVARCHAR(200)       NULL,");
        sb.AppendLine("        [IconName]               NVARCHAR(100)       NULL,");
        sb.AppendLine("        [RouterLink]             NVARCHAR(500)       NULL,");
        sb.AppendLine("        [NavigateUrl]            NVARCHAR(500)       NULL,");
        sb.AppendLine("        [Target]                 NVARCHAR(20)        NULL,");
        sb.AppendLine("        [Modified]               DATETIME2           NOT NULL    DEFAULT GETDATE(),");
        sb.AppendLine("        [ModifiedBy]             NVARCHAR(450)       NULL,");
        sb.AppendLine("        [VisibilityRules]        NVARCHAR(MAX)       NULL,");
        sb.AppendLine();
        sb.AppendLine("        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav] ([NavId]) ON DELETE CASCADE,");
        sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_DefaultNavItemId] FOREIGN KEY ([DefaultNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_DefaultParentNavItemId] FOREIGN KEY ([DefaultParentNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_ModifiedBy] FOREIGN KEY ([ModifiedBy]) REFERENCES [dbo].[AspNetUsers] ([Id])");
        sb.AppendLine("    );");
        sb.AppendLine("    PRINT 'Created table: nav.NavItem';");
        sb.AppendLine("END");
        sb.AppendLine();

        // ── 3. Clear existing data ──
        sb.AppendLine("-- ── 3. Clear existing platform default data ──");
        sb.AppendLine();
        sb.AppendLine("DELETE FROM [nav].[NavItem] WHERE [NavId] IN (SELECT [NavId] FROM [nav].[Nav] WHERE [JobId] IS NULL);");
        sb.AppendLine("DELETE FROM [nav].[Nav] WHERE [JobId] IS NULL;");
        sb.AppendLine("PRINT 'Cleared existing platform default navs and items';");
        sb.AppendLine();

        // ── 4. Insert Nav rows ──
        if (navs.Count > 0)
        {
            sb.AppendLine("-- ── 4. Insert Nav rows ──");
            sb.AppendLine();
            sb.AppendLine("SET IDENTITY_INSERT [nav].[Nav] ON;");
            sb.AppendLine();

            foreach (var nav in navs)
            {
                sb.AppendLine($"INSERT INTO [nav].[Nav] ([NavId], [RoleId], [JobId], [Active], [Modified])");
                sb.AppendLine($"VALUES ({nav.NavId}, '{SqlEscape(nav.RoleId)}', NULL, {(nav.Active ? 1 : 0)}, GETDATE());");
            }

            sb.AppendLine();
            sb.AppendLine("SET IDENTITY_INSERT [nav].[Nav] OFF;");
            sb.AppendLine($"PRINT 'Inserted {navs.Count} platform default nav(s)';");
            sb.AppendLine();
        }

        // ── 5. Insert NavItem rows (parents first, then children) ──
        var parentItems = new List<(int NavId, NavEditorNavItemDto Item)>();
        var childItems = new List<(int NavId, NavEditorNavItemDto Item)>();

        foreach (var nav in navs)
        {
            foreach (var item in nav.Items)
            {
                parentItems.Add((nav.NavId, item));
                foreach (var child in item.Children)
                {
                    childItems.Add((nav.NavId, child));
                }
            }
        }

        var allItems = parentItems.Concat(childItems).ToList();

        if (allItems.Count > 0)
        {
            sb.AppendLine("-- ── 5. Insert NavItem rows (parents first, then children) ──");
            sb.AppendLine();
            sb.AppendLine("SET IDENTITY_INSERT [nav].[NavItem] ON;");
            sb.AppendLine();

            foreach (var (navId, item) in allItems)
            {
                var parentCol = item.ParentNavItemId.HasValue
                    ? item.ParentNavItemId.Value.ToString()
                    : "NULL";
                var iconCol = item.IconName != null ? $"N'{SqlEscape(item.IconName)}'" : "NULL";
                var routerCol = item.RouterLink != null ? $"N'{SqlEscape(item.RouterLink)}'" : "NULL";
                var urlCol = item.NavigateUrl != null ? $"N'{SqlEscape(item.NavigateUrl)}'" : "NULL";
                var targetCol = item.Target != null ? $"N'{SqlEscape(item.Target)}'" : "NULL";
                var rulesCol = item.VisibilityRules != null ? $"N'{SqlEscape(item.VisibilityRules)}'" : "NULL";

                sb.AppendLine($"INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [VisibilityRules], [Modified])");
                var textCol = item.Text != null ? $"N'{SqlEscape(item.Text)}'" : "NULL";
                sb.AppendLine($"VALUES ({item.NavItemId}, {navId}, {parentCol}, {(item.Active ? 1 : 0)}, {item.SortOrder}, {textCol}, {iconCol}, {routerCol}, {urlCol}, {targetCol}, {rulesCol}, GETDATE());");
            }

            sb.AppendLine();
            sb.AppendLine("SET IDENTITY_INSERT [nav].[NavItem] OFF;");
            sb.AppendLine($"PRINT 'Inserted {allItems.Count} nav item(s) ({parentItems.Count} parents, {childItems.Count} children)';");
            sb.AppendLine();
        }

        // ── 6. Verification ──
        sb.AppendLine("-- ── 6. Verification ──");
        sb.AppendLine();
        sb.AppendLine("PRINT '';");
        sb.AppendLine("PRINT '════════════════════════════════════════════';");
        sb.AppendLine("PRINT ' Nav Defaults Export — Complete';");
        sb.AppendLine("PRINT '════════════════════════════════════════════';");
        sb.AppendLine("PRINT '';");
        sb.AppendLine();
        sb.AppendLine("SELECT");
        sb.AppendLine("    r.Name AS [Role],");
        sb.AppendLine("    n.NavId,");
        sb.AppendLine("    n.Active AS [NavActive],");
        sb.AppendLine("    parent.Text AS [Section],");
        sb.AppendLine("    parent.SortOrder AS [SectionOrder],");
        sb.AppendLine("    child.Text AS [Item],");
        sb.AppendLine("    child.SortOrder AS [ItemOrder],");
        sb.AppendLine("    child.IconName AS [Icon],");
        sb.AppendLine("    child.RouterLink AS [Route]");
        sb.AppendLine("FROM [nav].[Nav] n");
        sb.AppendLine("JOIN [dbo].[AspNetRoles] r ON n.RoleId = r.Id");
        sb.AppendLine("LEFT JOIN [nav].[NavItem] parent ON parent.NavId = n.NavId AND parent.ParentNavItemId IS NULL");
        sb.AppendLine("LEFT JOIN [nav].[NavItem] child  ON child.ParentNavItemId = parent.NavItemId");
        sb.AppendLine("WHERE n.JobId IS NULL");
        sb.AppendLine("ORDER BY r.Name, parent.SortOrder, child.SortOrder;");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT OFF;");

        return sb.ToString();
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

    public async Task<NavVisibilityOptionsDto> GetVisibilityOptionsAsync(CancellationToken ct = default)
    {
        return await _navEditorRepo.GetVisibilityOptionsAsync(ct);
    }

    /// <summary>Escape single quotes for SQL string literals.</summary>
    private static string SqlEscape(string value) => value.Replace("'", "''");
}
