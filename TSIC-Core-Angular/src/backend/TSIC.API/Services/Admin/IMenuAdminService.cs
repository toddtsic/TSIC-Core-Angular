using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Admin;

public interface IMenuAdminService
{
    Task<List<MenuAdminDto>> GetAllMenusAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task ToggleMenuActiveAsync(Guid menuId, bool active, string userId, CancellationToken cancellationToken = default);
    Task<MenuItemAdminDto> CreateMenuItemAsync(CreateMenuItemRequest request, string userId, CancellationToken cancellationToken = default);
    Task<MenuItemAdminDto> UpdateMenuItemAsync(Guid menuItemId, UpdateMenuItemRequest request, string userId, CancellationToken cancellationToken = default);
    Task DeleteMenuItemAsync(Guid menuItemId, CancellationToken cancellationToken = default);
    Task ReorderMenuItemsAsync(ReorderMenuItemsRequest request, string userId, CancellationToken cancellationToken = default);
    Task EnsureAllRoleMenusAsync(Guid jobId, string userId, CancellationToken cancellationToken = default);
}
