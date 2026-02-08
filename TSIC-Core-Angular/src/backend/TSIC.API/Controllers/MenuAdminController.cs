using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Application.Services.MenuAdmin;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/menu-admin")]
[Authorize(Policy = "SuperUserOnly")]
public class MenuAdminController : ControllerBase
{
    private readonly IMenuAdminService _menuAdminService;
    private readonly IJobLookupService _jobLookupService;

    public MenuAdminController(IMenuAdminService menuAdminService, IJobLookupService jobLookupService)
    {
        _menuAdminService = menuAdminService;
        _jobLookupService = jobLookupService;
    }

    /// <summary>
    /// Gets all role menus for the current user's job with nested item hierarchy.
    /// Includes inactive items for admin visibility.
    /// </summary>
    [HttpGet("menus")]
    public async Task<ActionResult<List<MenuAdminDto>>> GetMenus()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest("Job ID could not be determined from user token");

        var menus = await _menuAdminService.GetAllMenusAsync(jobId.Value);
        return Ok(menus);
    }

    /// <summary>
    /// Toggles the Active state of a menu (Level 0).
    /// </summary>
    [HttpPut("menus/{menuId:guid}/active")]
    public async Task<IActionResult> ToggleMenuActive(Guid menuId, [FromBody] UpdateMenuActiveRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        await _menuAdminService.ToggleMenuActiveAsync(menuId, request.Active, userId);
        return NoContent();
    }

    /// <summary>
    /// Creates a new menu item.
    /// Level 1 (ParentMenuItemId=null): Creates parent + auto-creates stub child.
    /// Level 2 (ParentMenuItemId set): Creates child under parent.
    /// </summary>
    [HttpPost("items")]
    public async Task<ActionResult<MenuItemAdminDto>> CreateMenuItem([FromBody] CreateMenuItemRequest request)
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest("Job ID could not be determined from user token");

        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var item = await _menuAdminService.CreateMenuItemAsync(jobId.Value, request, userId);
        return Ok(item);
    }

    /// <summary>
    /// Updates an existing menu item's properties.
    /// MenuId and ParentMenuItemId cannot be changed.
    /// </summary>
    [HttpPut("items/{menuItemId:guid}")]
    public async Task<ActionResult<MenuItemAdminDto>> UpdateMenuItem(Guid menuItemId, [FromBody] UpdateMenuItemRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var item = await _menuAdminService.UpdateMenuItemAsync(menuItemId, request, userId);
        return Ok(item);
    }

    /// <summary>
    /// Deletes a menu item.
    /// Hard delete if siblings exist, soft delete (Active=false) if last sibling.
    /// </summary>
    [HttpDelete("items/{menuItemId:guid}")]
    public async Task<IActionResult> DeleteMenuItem(Guid menuItemId)
    {
        await _menuAdminService.DeleteMenuItemAsync(menuItemId);
        return NoContent();
    }

    /// <summary>
    /// Reorders sibling menu items by assigning sequential Index values.
    /// </summary>
    [HttpPut("items/reorder")]
    public async Task<IActionResult> ReorderItems([FromBody] ReorderMenuItemsRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        await _menuAdminService.ReorderMenuItemsAsync(request, userId);
        return NoContent();
    }

    /// <summary>
    /// Ensures all 6 standard roles have menus for the current user's job.
    /// Creates missing role menus with stub parent/child items.
    /// </summary>
    [HttpPost("menus/ensure-all-roles")]
    public async Task<ActionResult<int>> EnsureAllRoleMenus()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest("Job ID could not be determined from user token");

        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var created = await _menuAdminService.EnsureAllRoleMenusAsync(jobId.Value, userId);
        return Ok(new { created });
    }
}
