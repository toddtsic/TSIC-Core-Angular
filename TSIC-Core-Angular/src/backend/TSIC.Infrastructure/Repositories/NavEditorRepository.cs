using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for SuperUser nav editor — CRUD for platform defaults,
/// job overrides, and legacy menu access for import.
/// </summary>
public class NavEditorRepository : INavEditorRepository
{
    private readonly SqlDbContext _context;

    public NavEditorRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ─── Platform defaults ──────────────────────────────────────────

    public async Task<List<NavEditorNavDto>> GetAllPlatformDefaultsAsync(
        CancellationToken cancellationToken = default)
    {
        var navs = await _context.Nav
            .AsNoTracking()
            .Where(n => n.JobId == null)
            .OrderBy(n => n.Role.Name)
            .Select(n => new NavEditorNavDto
            {
                NavId = n.NavId,
                RoleId = n.RoleId,
                RoleName = n.Role.Name,
                JobId = null,
                Active = n.Active,
                IsDefault = true,
                Items = new List<NavEditorNavItemDto>()
            })
            .ToListAsync(cancellationToken);

        // Load items for each nav
        foreach (var nav in navs)
        {
            nav.Items.AddRange(await LoadEditorNavItemTreeAsync(nav.NavId, cancellationToken));
        }

        return navs;
    }

    public async Task<Nav?> GetNavByIdAsync(int navId, CancellationToken cancellationToken = default)
    {
        return await _context.Nav
            .FirstOrDefaultAsync(n => n.NavId == navId, cancellationToken);
    }

    public async Task<bool> PlatformDefaultExistsAsync(
        string roleId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Nav
            .AsNoTracking()
            .AnyAsync(n => n.RoleId == roleId && n.JobId == null, cancellationToken);
    }

    // ─── Job overrides ──────────────────────────────────────────────

    public async Task<List<NavEditorNavDto>> GetJobOverridesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var navs = await _context.Nav
            .AsNoTracking()
            .Where(n => n.JobId == jobId)
            .OrderBy(n => n.Role.Name)
            .Select(n => new NavEditorNavDto
            {
                NavId = n.NavId,
                RoleId = n.RoleId,
                RoleName = n.Role.Name,
                JobId = n.JobId,
                Active = n.Active,
                IsDefault = false,
                Items = new List<NavEditorNavItemDto>()
            })
            .ToListAsync(cancellationToken);

        foreach (var nav in navs)
        {
            nav.Items.AddRange(await LoadEditorNavItemTreeAsync(nav.NavId, cancellationToken));
        }

        return navs;
    }

    // ─── Nav items ──────────────────────────────────────────────────

    public async Task<NavItem?> GetNavItemByIdAsync(
        int navItemId, CancellationToken cancellationToken = default)
    {
        return await _context.NavItem
            .FirstOrDefaultAsync(ni => ni.NavItemId == navItemId, cancellationToken);
    }

    public async Task<List<NavItem>> GetNavItemsByNavIdAsync(
        int navId, CancellationToken cancellationToken = default)
    {
        return await _context.NavItem
            .AsNoTracking()
            .Where(ni => ni.NavId == navId)
            .OrderBy(ni => ni.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NavItem>> GetSiblingItemsAsync(
        int navId, int? parentNavItemId, CancellationToken cancellationToken = default)
    {
        return await _context.NavItem
            .Where(ni => ni.NavId == navId && ni.ParentNavItemId == parentNavItemId)
            .OrderBy(ni => ni.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetSiblingCountAsync(
        int navId, int? parentNavItemId, CancellationToken cancellationToken = default)
    {
        return await _context.NavItem
            .CountAsync(ni => ni.NavId == navId && ni.ParentNavItemId == parentNavItemId, cancellationToken);
    }

    // ─── Legacy menu access ─────────────────────────────────────────

    public async Task<List<NavEditorLegacyMenuDto>> GetLegacyMenusForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var menus = await _context.JobMenus
            .AsNoTracking()
            .Where(m => m.JobId == jobId)
            .OrderBy(m => m.Role != null ? m.Role.Name : "zzz")
            .Select(m => new NavEditorLegacyMenuDto
            {
                MenuId = m.MenuId,
                JobId = m.JobId,
                RoleId = m.RoleId,
                RoleName = m.Role != null ? m.Role.Name : null,
                Active = m.Active,
                Items = new List<NavEditorLegacyItemDto>()
            })
            .ToListAsync(cancellationToken);

        foreach (var menu in menus)
        {
            menu.Items.AddRange(await LoadLegacyMenuItemTreeAsync(menu.MenuId, cancellationToken));
        }

        return menus;
    }

    // ─── Mutations ──────────────────────────────────────────────────

    public void AddNav(Nav nav) => _context.Nav.Add(nav);

    public void AddNavItem(NavItem navItem) => _context.NavItem.Add(navItem);

    public void RemoveNavItem(NavItem navItem) => _context.NavItem.Remove(navItem);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ─── Private helpers ────────────────────────────────────────────

    private async Task<List<NavEditorNavItemDto>> LoadEditorNavItemTreeAsync(
        int navId, CancellationToken cancellationToken)
    {
        // Load root items (includes inactive for editor)
        var rootItems = await _context.NavItem
            .AsNoTracking()
            .Where(ni => ni.NavId == navId && ni.ParentNavItemId == null)
            .OrderBy(ni => ni.SortOrder)
            .Select(ni => new NavEditorNavItemDto
            {
                NavItemId = ni.NavItemId,
                NavId = ni.NavId,
                ParentNavItemId = null,
                SortOrder = ni.SortOrder,
                Text = ni.Text,
                IconName = ni.IconName,
                RouterLink = ni.RouterLink,
                NavigateUrl = ni.NavigateUrl,
                Target = ni.Target,
                Active = ni.Active,
                Children = new List<NavEditorNavItemDto>()
            })
            .ToListAsync(cancellationToken);

        if (rootItems.Count == 0) return rootItems;

        var rootIds = rootItems.Select(r => r.NavItemId).ToList();
        var childItems = await _context.NavItem
            .AsNoTracking()
            .Where(ni => ni.NavId == navId && ni.ParentNavItemId != null && rootIds.Contains(ni.ParentNavItemId.Value))
            .OrderBy(ni => ni.SortOrder)
            .Select(ni => new NavEditorNavItemDto
            {
                NavItemId = ni.NavItemId,
                NavId = ni.NavId,
                ParentNavItemId = ni.ParentNavItemId,
                SortOrder = ni.SortOrder,
                Text = ni.Text,
                IconName = ni.IconName,
                RouterLink = ni.RouterLink,
                NavigateUrl = ni.NavigateUrl,
                Target = ni.Target,
                Active = ni.Active,
                Children = new List<NavEditorNavItemDto>()
            })
            .ToListAsync(cancellationToken);

        foreach (var child in childItems)
        {
            var parent = rootItems.FirstOrDefault(r => r.NavItemId == child.ParentNavItemId);
            parent?.Children.Add(child);
        }

        return rootItems;
    }

    private async Task<List<NavEditorLegacyItemDto>> LoadLegacyMenuItemTreeAsync(
        Guid menuId, CancellationToken cancellationToken)
    {
        var rootItems = await _context.JobMenuItems
            .AsNoTracking()
            .Where(mi => mi.MenuId == menuId && mi.ParentMenuItemId == null)
            .OrderBy(mi => mi.Index)
            .Select(mi => new NavEditorLegacyItemDto
            {
                MenuItemId = mi.MenuItemId,
                ParentMenuItemId = null,
                Index = mi.Index,
                Text = mi.Text,
                IconName = mi.IconName,
                RouterLink = mi.RouterLink,
                NavigateUrl = mi.NavigateUrl,
                Controller = mi.Controller,
                Action = mi.Action,
                Target = mi.Target,
                Active = mi.Active,
                Children = new List<NavEditorLegacyItemDto>()
            })
            .ToListAsync(cancellationToken);

        if (rootItems.Count == 0) return rootItems;

        var rootIds = rootItems.Select(r => r.MenuItemId).ToList();
        var childItems = await _context.JobMenuItems
            .AsNoTracking()
            .Where(mi => mi.MenuId == menuId && mi.ParentMenuItemId != null && rootIds.Contains(mi.ParentMenuItemId.Value))
            .OrderBy(mi => mi.Index)
            .Select(mi => new NavEditorLegacyItemDto
            {
                MenuItemId = mi.MenuItemId,
                ParentMenuItemId = mi.ParentMenuItemId,
                Index = mi.Index,
                Text = mi.Text,
                IconName = mi.IconName,
                RouterLink = mi.RouterLink,
                NavigateUrl = mi.NavigateUrl,
                Controller = mi.Controller,
                Action = mi.Action,
                Target = mi.Target,
                Active = mi.Active,
                Children = new List<NavEditorLegacyItemDto>()
            })
            .ToListAsync(cancellationToken);

        foreach (var child in childItems)
        {
            var parent = rootItems.FirstOrDefault(r => r.MenuItemId == child.ParentMenuItemId);
            parent?.Children.Add(child);
        }

        return rootItems;
    }
}
