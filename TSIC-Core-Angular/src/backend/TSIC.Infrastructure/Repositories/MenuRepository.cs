using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IMenuRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for JobMenus and JobMenuItems entities.
/// </summary>
public class MenuRepository : IMenuRepository
{
    private readonly SqlDbContext _context;
    private readonly Func<string?, string?, bool> _checkRouteImplemented;

    public MenuRepository(SqlDbContext context, Func<string?, string?, bool> checkRouteImplemented)
    {
        _context = context;
        _checkRouteImplemented = checkRouteImplemented;
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
                IsImplemented = false, // Will be set below
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
                IsImplemented = false, // Will be set below
                Children = new List<MenuItemDto>()
            })
            .ToListAsync(cancellationToken);

        // Check route availability for all items
        foreach (var item in rootItems.Concat(childItems))
        {
            item.IsImplemented = _checkRouteImplemented(item.Controller, item.Action);
        }

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
}
