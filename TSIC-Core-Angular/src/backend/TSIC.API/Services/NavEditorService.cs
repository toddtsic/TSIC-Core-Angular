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
        sb.AppendLine("        [NavItemId]         INT IDENTITY(1,1)   NOT NULL,");
        sb.AppendLine("        [NavId]             INT                 NOT NULL,");
        sb.AppendLine("        [ParentNavItemId]   INT                 NULL,");
        sb.AppendLine("        [Active]            BIT                 NOT NULL    DEFAULT 1,");
        sb.AppendLine("        [SortOrder]         INT                 NOT NULL    DEFAULT 0,");
        sb.AppendLine("        [Text]              NVARCHAR(200)       NOT NULL,");
        sb.AppendLine("        [IconName]          NVARCHAR(100)       NULL,");
        sb.AppendLine("        [RouterLink]        NVARCHAR(500)       NULL,");
        sb.AppendLine("        [NavigateUrl]       NVARCHAR(500)       NULL,");
        sb.AppendLine("        [Target]            NVARCHAR(20)        NULL,");
        sb.AppendLine("        [Modified]          DATETIME2           NOT NULL    DEFAULT GETDATE(),");
        sb.AppendLine("        [ModifiedBy]        NVARCHAR(450)       NULL,");
        sb.AppendLine();
        sb.AppendLine("        CONSTRAINT [PK_nav_NavItem] PRIMARY KEY CLUSTERED ([NavItemId]),");
        sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_NavId] FOREIGN KEY ([NavId]) REFERENCES [nav].[Nav] ([NavId]) ON DELETE CASCADE,");
        sb.AppendLine("        CONSTRAINT [FK_nav_NavItem_ParentNavItemId] FOREIGN KEY ([ParentNavItemId]) REFERENCES [nav].[NavItem] ([NavItemId]),");
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

                sb.AppendLine($"INSERT INTO [nav].[NavItem] ([NavItemId], [NavId], [ParentNavItemId], [Active], [SortOrder], [Text], [IconName], [RouterLink], [NavigateUrl], [Target], [Modified])");
                sb.AppendLine($"VALUES ({item.NavItemId}, {navId}, {parentCol}, {(item.Active ? 1 : 0)}, {item.SortOrder}, N'{SqlEscape(item.Text)}', {iconCol}, {routerCol}, {urlCol}, {targetCol}, GETDATE());");
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

    /// <summary>Escape single quotes for SQL string literals.</summary>
    private static string SqlEscape(string value) => value.Replace("'", "''");
}
