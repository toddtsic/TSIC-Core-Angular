using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IMenuRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for JobMenus and JobMenuItems entities.
/// </summary>
public class MenuRepository : IMenuRepository
{
    private readonly SqlDbContext _context;

    public MenuRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<MenuDto?> GetMenuForJobAndRoleAsync(
        Guid jobId,
        string? roleName = null,
        CancellationToken cancellationToken = default)
    {
        // Precedence: Role-specific (active) → Anonymous (roleId NULL, active) → null
        var menu = await _context.JobMenus
            .AsNoTracking()
            .Where(m => m.JobId == jobId && m.Active == true)
            .Where(m => roleName != null
                ? m.Role != null && m.Role.Name == roleName
                : m.RoleId == null)
            .OrderBy(m => m.RoleId != null ? 0 : 1) // Role-specific first
            .FirstOrDefaultAsync(cancellationToken);

        // If no role-specific menu found and roleName was provided, try anonymous fallback
        if (menu == null && roleName != null)
        {
            menu = await _context.JobMenus
                .AsNoTracking()
                .Where(m => m.JobId == jobId && m.Active == true && m.RoleId == null)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (menu == null)
        {
            return null;
        }

        // Load root menu items (no parent)
        var rootItems = await _context.JobMenuItems
            .AsNoTracking()
            .Where(mi => mi.MenuId == menu.MenuId && mi.Active == true && mi.ParentMenuItemId == null)
            .OrderBy(mi => mi.Index)
            .Select(mi => new MenuItemDto
            {
                MenuItemId = mi.MenuItemId,
                ParentMenuItemId = mi.ParentMenuItemId,
                Index = mi.Index,
                Text = mi.Text,
                IconName = mi.IconName,
                BCollapsed = mi.BCollapsed,
                BTextWrap = mi.BTextWrap,
                RouterLink = mi.RouterLink,
                NavigateUrl = mi.NavigateUrl,
                Controller = mi.Controller,
                Action = mi.Action,
                LinkTarget = mi.Target,
                Children = new List<MenuItemDto>()
            })
            .ToListAsync(cancellationToken);

        // Load all child items in one query
        var rootItemIds = rootItems.Select(r => r.MenuItemId).ToList();
        var childItems = await _context.JobMenuItems
            .AsNoTracking()
            .Where(mi => mi.MenuId == menu.MenuId && mi.Active == true && mi.ParentMenuItemId != null && rootItemIds.Contains(mi.ParentMenuItemId.Value))
            .OrderBy(mi => mi.Index)
            .Select(mi => new MenuItemDto
            {
                MenuItemId = mi.MenuItemId,
                ParentMenuItemId = mi.ParentMenuItemId,
                Index = mi.Index,
                Text = mi.Text,
                IconName = mi.IconName,
                BCollapsed = mi.BCollapsed,
                BTextWrap = mi.BTextWrap,
                RouterLink = mi.RouterLink,
                NavigateUrl = mi.NavigateUrl,
                Controller = mi.Controller,
                Action = mi.Action,
                LinkTarget = mi.Target,
                Children = new List<MenuItemDto>()
            })
            .ToListAsync(cancellationToken);

        // Populate children into parent items
        foreach (var child in childItems)
        {
            var parent = rootItems.FirstOrDefault(r => r.MenuItemId == child.ParentMenuItemId);
            if (parent != null)
            {
                parent.Children.Add(child);
            }
        }

        return new MenuDto
        {
            MenuId = menu.MenuId,
            JobId = menu.JobId,
            RoleId = menu.RoleId,
            MenuTypeId = menu.MenuTypeId,
            Tag = menu.Tag,
            Items = rootItems
        };
    }

    // ─── Admin methods ────────────────────────────────────────────────

    public async Task<List<JobMenus>> GetAllMenusForJobAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.JobMenus
            .AsNoTracking()
            .Include(m => m.Role)
            .Where(m => m.JobId == jobId)
            .OrderBy(m => m.Role != null ? m.Role.Name : "zzz")
            .ToListAsync(cancellationToken);
    }

    public async Task<JobMenus?> GetMenuByIdAsync(
        Guid menuId, CancellationToken cancellationToken = default)
    {
        return await _context.JobMenus
            .FirstOrDefaultAsync(m => m.MenuId == menuId, cancellationToken);
    }

    public async Task<JobMenuItems?> GetMenuItemByIdAsync(
        Guid menuItemId, CancellationToken cancellationToken = default)
    {
        return await _context.JobMenuItems
            .FirstOrDefaultAsync(mi => mi.MenuItemId == menuItemId, cancellationToken);
    }

    public async Task<List<JobMenuItems>> GetMenuItemsByMenuIdAsync(
        Guid menuId, CancellationToken cancellationToken = default)
    {
        return await _context.JobMenuItems
            .AsNoTracking()
            .Where(mi => mi.MenuId == menuId)
            .OrderBy(mi => mi.Index)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<JobMenuItems>> GetSiblingItemsAsync(
        Guid menuId, Guid? parentMenuItemId, CancellationToken cancellationToken = default)
    {
        return await _context.JobMenuItems
            .Where(mi => mi.MenuId == menuId && mi.ParentMenuItemId == parentMenuItemId)
            .OrderBy(mi => mi.Index)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetSiblingCountAsync(
        Guid menuId, Guid? parentMenuItemId, CancellationToken cancellationToken = default)
    {
        return await _context.JobMenuItems
            .CountAsync(mi => mi.MenuId == menuId && mi.ParentMenuItemId == parentMenuItemId, cancellationToken);
    }

    public async Task<List<string>> GetExistingMenuRoleIdsForJobAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.JobMenus
            .AsNoTracking()
            .Where(m => m.JobId == jobId && m.RoleId != null)
            .Select(m => m.RoleId!)
            .ToListAsync(cancellationToken);
    }

    public void AddMenu(JobMenus menu) => _context.JobMenus.Add(menu);

    public void AddMenuItem(JobMenuItems menuItem) => _context.JobMenuItems.Add(menuItem);

    public void RemoveMenuItem(JobMenuItems menuItem) => _context.JobMenuItems.Remove(menuItem);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
