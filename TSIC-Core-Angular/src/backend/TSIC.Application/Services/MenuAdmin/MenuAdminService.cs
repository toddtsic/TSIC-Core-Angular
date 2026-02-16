using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Application.Services.MenuAdmin;

public class MenuAdminService : IMenuAdminService
{
    private readonly IMenuRepository _menuRepo;

    public MenuAdminService(IMenuRepository menuRepo)
    {
        _menuRepo = menuRepo;
    }

    public async Task<List<MenuAdminDto>> GetAllMenusAsync(Guid jobId)
    {
        var menus = await _menuRepo.GetAllMenusForJobAsync(jobId);
        var result = new List<MenuAdminDto>();

        foreach (var menu in menus)
        {
            var items = await _menuRepo.GetMenuItemsByMenuIdAsync(menu.MenuId);

            // Build hierarchical tree: root items where ParentMenuItemId == null
            var rootItems = items.Where(i => i.ParentMenuItemId == null).OrderBy(i => i.Index).ToList();
            var menuDto = new MenuAdminDto
            {
                MenuId = menu.MenuId,
                JobId = menu.JobId,
                RoleId = menu.RoleId,
                RoleName = menu.RoleName ?? "Unknown",
                Active = menu.Active,
                MenuTypeId = menu.MenuTypeId,
                Items = rootItems.Select(parent => MapToMenuItemDto(parent, items)).ToList()
            };

            result.Add(menuDto);
        }

        return result;
    }

    public async Task ToggleMenuActiveAsync(Guid menuId, bool active, string userId)
    {
        var menu = await _menuRepo.GetMenuByIdAsync(menuId);
        if (menu == null)
            throw new InvalidOperationException($"Menu {menuId} not found");

        menu.Active = active;
        menu.Modified = DateTime.UtcNow;
        menu.LebUserId = userId;

        await _menuRepo.SaveChangesAsync();
    }

    public async Task<MenuItemAdminDto> CreateMenuItemAsync(Guid jobId, CreateMenuItemRequest request, string userId)
    {
        var now = DateTime.UtcNow;

        if (request.ParentMenuItemId == null)
        {
            // Level 1: Create parent item
            var siblingCount = await _menuRepo.GetSiblingCountAsync(request.MenuId, null);
            var parent = new JobMenuItems
            {
                MenuItemId = Guid.NewGuid(),
                MenuId = request.MenuId,
                ParentMenuItemId = null,
                Text = request.Text,
                IconName = request.IconName,
                RouterLink = request.RouterLink,
                NavigateUrl = request.NavigateUrl,
                Controller = request.Controller,
                Action = request.Action,
                Target = request.Target,
                Active = request.Active,
                Index = siblingCount + 1,
                Modified = now,
                LebUserId = userId
            };
            _menuRepo.AddMenuItem(parent);

            // Auto-create stub child
            var child = new JobMenuItems
            {
                MenuItemId = Guid.NewGuid(),
                MenuId = request.MenuId,
                ParentMenuItemId = parent.MenuItemId,
                Text = "new child",
                Active = false,
                Index = 1,
                Modified = now,
                LebUserId = userId
            };
            _menuRepo.AddMenuItem(child);

            await _menuRepo.SaveChangesAsync();

            return new MenuItemAdminDto
            {
                MenuItemId = parent.MenuItemId,
                MenuId = parent.MenuId ?? Guid.Empty,
                ParentMenuItemId = parent.ParentMenuItemId,
                Text = parent.Text ?? string.Empty,
                IconName = parent.IconName,
                RouterLink = parent.RouterLink,
                NavigateUrl = parent.NavigateUrl,
                Controller = parent.Controller,
                Action = parent.Action,
                Target = parent.Target,
                Active = parent.Active,
                Index = parent.Index ?? 0,
                Children = new List<MenuItemAdminDto>
                {
                    new MenuItemAdminDto
                    {
                        MenuItemId = child.MenuItemId,
                        MenuId = child.MenuId ?? Guid.Empty,
                        ParentMenuItemId = child.ParentMenuItemId,
                        Text = child.Text ?? string.Empty,
                        Active = child.Active,
                        Index = child.Index ?? 0,
                        Children = new List<MenuItemAdminDto>()
                    }
                }
            };
        }
        else
        {
            // Level 2: Create child item
            var siblingCount = await _menuRepo.GetSiblingCountAsync(request.MenuId, request.ParentMenuItemId);
            var child = new JobMenuItems
            {
                MenuItemId = Guid.NewGuid(),
                MenuId = request.MenuId,
                ParentMenuItemId = request.ParentMenuItemId,
                Text = request.Text,
                IconName = request.IconName,
                RouterLink = request.RouterLink,
                NavigateUrl = request.NavigateUrl,
                Controller = request.Controller,
                Action = request.Action,
                Target = request.Target,
                Active = request.Active,
                Index = siblingCount + 1,
                Modified = now,
                LebUserId = userId
            };
            _menuRepo.AddMenuItem(child);
            await _menuRepo.SaveChangesAsync();

            return new MenuItemAdminDto
            {
                MenuItemId = child.MenuItemId,
                MenuId = child.MenuId ?? Guid.Empty,
                ParentMenuItemId = child.ParentMenuItemId,
                Text = child.Text ?? string.Empty,
                IconName = child.IconName,
                RouterLink = child.RouterLink,
                NavigateUrl = child.NavigateUrl,
                Controller = child.Controller,
                Action = child.Action,
                Target = child.Target,
                Active = child.Active,
                Index = child.Index ?? 0,
                Children = new List<MenuItemAdminDto>()
            };
        }
    }

    public async Task<MenuItemAdminDto> UpdateMenuItemAsync(Guid menuItemId, UpdateMenuItemRequest request, string userId)
    {
        var item = await _menuRepo.GetMenuItemByIdAsync(menuItemId);
        if (item == null)
            throw new InvalidOperationException($"Menu item {menuItemId} not found");

        item.Text = request.Text;
        item.IconName = request.IconName;
        item.RouterLink = request.RouterLink;
        item.NavigateUrl = request.NavigateUrl;
        item.Controller = request.Controller;
        item.Action = request.Action;
        item.Target = request.Target;
        item.Active = request.Active;
        item.Modified = DateTime.UtcNow;
        item.LebUserId = userId;

        await _menuRepo.SaveChangesAsync();

        return new MenuItemAdminDto
        {
            MenuItemId = item.MenuItemId,
            MenuId = item.MenuId ?? Guid.Empty,
            ParentMenuItemId = item.ParentMenuItemId,
            Text = item.Text ?? string.Empty,
            IconName = item.IconName,
            RouterLink = item.RouterLink,
            NavigateUrl = item.NavigateUrl,
            Controller = item.Controller,
            Action = item.Action,
            Target = item.Target,
            Active = item.Active,
            Index = item.Index ?? 0,
            Children = new List<MenuItemAdminDto>()
        };
    }

    public async Task DeleteMenuItemAsync(Guid menuItemId)
    {
        var item = await _menuRepo.GetMenuItemByIdAsync(menuItemId);
        if (item == null)
            throw new InvalidOperationException($"Menu item {menuItemId} not found");

        var siblingCount = await _menuRepo.GetSiblingCountAsync(item.MenuId ?? Guid.Empty, item.ParentMenuItemId);

        if (siblingCount > 1)
        {
            // Hard delete - has siblings
            _menuRepo.RemoveMenuItem(item);
        }
        else
        {
            // Soft delete - last sibling, prevent orphaning
            item.Active = false;
            item.Modified = DateTime.UtcNow;
        }

        await _menuRepo.SaveChangesAsync();
    }

    public async Task ReorderMenuItemsAsync(ReorderMenuItemsRequest request, string userId)
    {
        var siblings = await _menuRepo.GetSiblingItemsAsync(request.MenuId, request.ParentMenuItemId);
        var now = DateTime.UtcNow;

        // Assign sequential indexes based on ordered list
        for (int i = 0; i < request.OrderedItemIds.Count; i++)
        {
            var item = siblings.FirstOrDefault(s => s.MenuItemId == request.OrderedItemIds[i]);
            if (item != null)
            {
                item.Index = i + 1;
                item.Modified = now;
                item.LebUserId = userId;
            }
        }

        await _menuRepo.SaveChangesAsync();
    }

    public async Task<int> EnsureAllRoleMenusAsync(Guid jobId, string userId)
    {
        var existingRoleIds = await _menuRepo.GetExistingMenuRoleIdsForJobAsync(jobId);
        var now = DateTime.UtcNow;

        // 6 standard roles for per-login-role menus
        var standardRoles = new[]
        {
            RoleConstants.Superuser,
            RoleConstants.Director,
            RoleConstants.Staff,
            RoleConstants.Player,
            RoleConstants.ClubRep,
            RoleConstants.Anonymous
        };

        var missingRoles = standardRoles.Except(existingRoleIds).ToList();
        int created = 0;

        foreach (var roleId in missingRoles)
        {
            // Create menu
            var menu = new JobMenus
            {
                MenuId = Guid.NewGuid(),
                JobId = jobId,
                RoleId = roleId,
                MenuTypeId = MenuTypeConstants.PerLoginRole,
                Active = false,
                Modified = now,
                LebUserId = userId
            };
            _menuRepo.AddMenu(menu);

            // Create stub parent item
            var parent = new JobMenuItems
            {
                MenuItemId = Guid.NewGuid(),
                MenuId = menu.MenuId,
                ParentMenuItemId = null,
                Text = "New Parent",
                Active = false,
                Index = 1,
                Modified = now,
                LebUserId = userId
            };
            _menuRepo.AddMenuItem(parent);

            // Create stub child item
            var child = new JobMenuItems
            {
                MenuItemId = Guid.NewGuid(),
                MenuId = menu.MenuId,
                ParentMenuItemId = parent.MenuItemId,
                Text = "new child",
                Active = false,
                Index = 1,
                Modified = now,
                LebUserId = userId
            };
            _menuRepo.AddMenuItem(child);

            created++;
        }

        if (created > 0)
        {
            await _menuRepo.SaveChangesAsync();
        }

        return created;
    }

    private MenuItemAdminDto MapToMenuItemDto(JobMenuItems item, List<JobMenuItems> allItems)
    {
        var children = allItems
            .Where(i => i.ParentMenuItemId == item.MenuItemId)
            .OrderBy(i => i.Index)
            .Select(child => MapToMenuItemDto(child, allItems))
            .ToList();

        return new MenuItemAdminDto
        {
            MenuItemId = item.MenuItemId,
            MenuId = item.MenuId ?? Guid.Empty,
            ParentMenuItemId = item.ParentMenuItemId,
            Text = item.Text ?? string.Empty,
            IconName = item.IconName,
            RouterLink = item.RouterLink,
            NavigateUrl = item.NavigateUrl,
            Controller = item.Controller,
            Action = item.Action,
            Target = item.Target,
            Active = item.Active,
            Index = item.Index ?? 0,
            Children = children
        };
    }
}
