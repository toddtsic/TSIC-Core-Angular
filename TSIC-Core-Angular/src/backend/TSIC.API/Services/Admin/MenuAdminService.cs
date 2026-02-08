using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

public sealed class MenuAdminService : IMenuAdminService
{
    private readonly IMenuRepository _menuRepo;

    /// <summary>
    /// The 6 roles that get auto-created menus via EnsureAllRoleMenus.
    /// </summary>
    private static readonly Dictionary<string, string> MenuRoles = new()
    {
        [RoleConstants.Superuser] = "Superuser",
        [RoleConstants.Director] = "Director",
        [RoleConstants.Staff] = "Staff",
        [RoleConstants.Player] = "Player",
        [RoleConstants.ClubRep] = "Club Rep",
        [RoleConstants.Anonymous] = "Anonymous"
    };

    public MenuAdminService(IMenuRepository menuRepo)
    {
        _menuRepo = menuRepo;
    }

    public async Task<List<MenuAdminDto>> GetAllMenusAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var menus = await _menuRepo.GetAllMenusForJobAsync(jobId, cancellationToken);
        var result = new List<MenuAdminDto>();

        foreach (var menu in menus)
        {
            var allItems = await _menuRepo.GetMenuItemsByMenuIdAsync(menu.MenuId, cancellationToken);

            // Build hierarchical tree
            var rootItems = allItems
                .Where(mi => mi.ParentMenuItemId == null)
                .OrderBy(mi => mi.Index)
                .Select(mi => MapToAdminDto(mi, allItems))
                .ToList();

            result.Add(new MenuAdminDto
            {
                MenuId = menu.MenuId,
                JobId = menu.JobId,
                RoleId = menu.RoleId,
                RoleName = menu.Role?.Name,
                Active = menu.Active,
                MenuTypeId = menu.MenuTypeId,
                Items = rootItems
            });
        }

        return result;
    }

    public async Task ToggleMenuActiveAsync(
        Guid menuId, bool active, string userId, CancellationToken cancellationToken = default)
    {
        var menu = await _menuRepo.GetMenuByIdAsync(menuId, cancellationToken)
            ?? throw new KeyNotFoundException($"Menu {menuId} not found");

        menu.Active = active;
        menu.Modified = DateTime.UtcNow;
        menu.LebUserId = userId;

        await _menuRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task<MenuItemAdminDto> CreateMenuItemAsync(
        CreateMenuItemRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var siblingCount = await _menuRepo.GetSiblingCountAsync(
            request.MenuId, request.ParentMenuItemId, cancellationToken);

        var newItem = new JobMenuItems
        {
            MenuItemId = Guid.NewGuid(),
            MenuId = request.MenuId,
            ParentMenuItemId = request.ParentMenuItemId,
            Text = request.Text,
            Active = request.Active,
            IconName = request.IconName,
            RouterLink = request.RouterLink,
            NavigateUrl = request.NavigateUrl,
            Controller = request.Controller,
            Action = request.Action,
            Target = request.Target,
            Index = siblingCount + 1,
            BCollapsed = false,
            BTextWrap = false,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _menuRepo.AddMenuItem(newItem);

        // If creating a parent (Level 1), auto-create stub child
        if (request.ParentMenuItemId == null)
        {
            var stubChild = new JobMenuItems
            {
                MenuItemId = Guid.NewGuid(),
                MenuId = request.MenuId,
                ParentMenuItemId = newItem.MenuItemId,
                Text = "new child",
                Active = false,
                Index = 1,
                BCollapsed = false,
                BTextWrap = false,
                Modified = DateTime.UtcNow,
                LebUserId = userId
            };
            _menuRepo.AddMenuItem(stubChild);
        }

        await _menuRepo.SaveChangesAsync(cancellationToken);

        // Return the created item (re-fetch children for parent items)
        var children = new List<MenuItemAdminDto>();
        if (request.ParentMenuItemId == null)
        {
            var childItems = await _menuRepo.GetSiblingItemsAsync(
                request.MenuId, newItem.MenuItemId, cancellationToken);
            children = childItems.Select(c => MapToAdminDto(c, new List<JobMenuItems>())).ToList();
        }

        return new MenuItemAdminDto
        {
            MenuItemId = newItem.MenuItemId,
            MenuId = newItem.MenuId ?? Guid.Empty,
            ParentMenuItemId = newItem.ParentMenuItemId,
            Text = newItem.Text,
            IconName = newItem.IconName,
            RouterLink = newItem.RouterLink,
            NavigateUrl = newItem.NavigateUrl,
            Controller = newItem.Controller,
            Action = newItem.Action,
            Target = newItem.Target,
            Active = newItem.Active,
            Index = newItem.Index,
            Children = children
        };
    }

    public async Task<MenuItemAdminDto> UpdateMenuItemAsync(
        Guid menuItemId, UpdateMenuItemRequest request, string userId,
        CancellationToken cancellationToken = default)
    {
        var item = await _menuRepo.GetMenuItemByIdAsync(menuItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Menu item {menuItemId} not found");

        item.Text = request.Text;
        item.Active = request.Active;
        item.IconName = request.IconName;
        item.RouterLink = request.RouterLink;
        item.NavigateUrl = request.NavigateUrl;
        item.Controller = request.Controller;
        item.Action = request.Action;
        item.Target = request.Target;
        item.Modified = DateTime.UtcNow;
        item.LebUserId = userId;

        await _menuRepo.SaveChangesAsync(cancellationToken);

        return new MenuItemAdminDto
        {
            MenuItemId = item.MenuItemId,
            MenuId = item.MenuId ?? Guid.Empty,
            ParentMenuItemId = item.ParentMenuItemId,
            Text = item.Text,
            IconName = item.IconName,
            RouterLink = item.RouterLink,
            NavigateUrl = item.NavigateUrl,
            Controller = item.Controller,
            Action = item.Action,
            Target = item.Target,
            Active = item.Active,
            Index = item.Index,
            Children = new List<MenuItemAdminDto>()
        };
    }

    public async Task DeleteMenuItemAsync(
        Guid menuItemId, CancellationToken cancellationToken = default)
    {
        var item = await _menuRepo.GetMenuItemByIdAsync(menuItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Menu item {menuItemId} not found");

        var menuId = item.MenuId ?? throw new InvalidOperationException("Menu item has no MenuId");
        var siblingCount = await _menuRepo.GetSiblingCountAsync(
            menuId, item.ParentMenuItemId, cancellationToken);

        if (siblingCount > 1)
        {
            // Hard delete — siblings remain
            _menuRepo.RemoveMenuItem(item);
        }
        else
        {
            // Soft delete — last sibling, just deactivate
            item.Active = false;
            item.Modified = DateTime.UtcNow;
        }

        await _menuRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderMenuItemsAsync(
        ReorderMenuItemsRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var siblings = await _menuRepo.GetSiblingItemsAsync(
            request.MenuId, request.ParentMenuItemId, cancellationToken);

        var lookup = siblings.ToDictionary(s => s.MenuItemId);
        var now = DateTime.UtcNow;

        for (var i = 0; i < request.OrderedItemIds.Count; i++)
        {
            if (lookup.TryGetValue(request.OrderedItemIds[i], out var item))
            {
                item.Index = i + 1;
                item.Modified = now;
                item.LebUserId = userId;
            }
        }

        await _menuRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureAllRoleMenusAsync(
        Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        var existingRoleIds = await _menuRepo.GetExistingMenuRoleIdsForJobAsync(jobId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var (roleId, _) in MenuRoles)
        {
            if (existingRoleIds.Contains(roleId, StringComparer.OrdinalIgnoreCase))
                continue;

            // Create menu
            var menu = new JobMenus
            {
                MenuId = Guid.NewGuid(),
                JobId = jobId,
                RoleId = roleId,
                MenuTypeId = 6,
                Active = false,
                Modified = now,
                LebUserId = userId
            };
            _menuRepo.AddMenu(menu);

            // Create stub parent
            var parentId = Guid.NewGuid();
            _menuRepo.AddMenuItem(new JobMenuItems
            {
                MenuItemId = parentId,
                MenuId = menu.MenuId,
                ParentMenuItemId = null,
                Text = "new parent item",
                Active = false,
                Index = 1,
                BCollapsed = false,
                BTextWrap = false,
                Modified = now,
                LebUserId = userId
            });

            // Create stub child
            _menuRepo.AddMenuItem(new JobMenuItems
            {
                MenuItemId = Guid.NewGuid(),
                MenuId = menu.MenuId,
                ParentMenuItemId = parentId,
                Text = "new child text",
                Active = false,
                Index = 1,
                BCollapsed = false,
                BTextWrap = false,
                Modified = now,
                LebUserId = userId
            });
        }

        await _menuRepo.SaveChangesAsync(cancellationToken);
    }

    private static MenuItemAdminDto MapToAdminDto(JobMenuItems item, List<JobMenuItems> allItems)
    {
        var children = allItems
            .Where(mi => mi.ParentMenuItemId == item.MenuItemId)
            .OrderBy(mi => mi.Index)
            .Select(mi => MapToAdminDto(mi, allItems))
            .ToList();

        return new MenuItemAdminDto
        {
            MenuItemId = item.MenuItemId,
            MenuId = item.MenuId ?? Guid.Empty,
            ParentMenuItemId = item.ParentMenuItemId,
            Text = item.Text,
            IconName = item.IconName,
            RouterLink = item.RouterLink,
            NavigateUrl = item.NavigateUrl,
            Controller = item.Controller,
            Action = item.Action,
            Target = item.Target,
            Active = item.Active,
            Index = item.Index,
            Children = children
        };
    }
}
