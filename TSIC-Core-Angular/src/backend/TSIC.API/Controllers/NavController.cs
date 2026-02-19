using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Controllers;

/// <summary>
/// Nav endpoints — read-only merged nav for rendering (authenticated)
/// and SuperUser editor endpoints.
/// </summary>
[ApiController]
[Route("api/nav")]
[Authorize]
public class NavController : ControllerBase
{
    private readonly INavRepository _navRepo;
    private readonly INavEditorService _navEditorService;
    private readonly IJobLookupService _jobLookupService;

    private static readonly Dictionary<string, string> RoleNameToIdMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Director"] = RoleConstants.Director,
        ["SuperDirector"] = RoleConstants.SuperDirector,
        ["Superuser"] = RoleConstants.Superuser,
        ["Family"] = RoleConstants.Family,
        ["Player"] = RoleConstants.Player,
        ["Club Rep"] = RoleConstants.ClubRep,
        ["Ref Assignor"] = RoleConstants.RefAssignor,
        ["Staff"] = RoleConstants.Staff,
        ["Store Admin"] = RoleConstants.StoreAdmin,
        ["STPAdmin"] = RoleConstants.StpAdmin,
    };

    public NavController(
        INavRepository navRepo,
        INavEditorService navEditorService,
        IJobLookupService jobLookupService)
    {
        _navRepo = navRepo;
        _navEditorService = navEditorService;
        _jobLookupService = jobLookupService;
    }

    // ─── Public read endpoint ───────────────────────────────────────

    /// <summary>
    /// Get the merged nav (platform defaults + job overrides) for the
    /// current user's role and job.
    /// </summary>
    [HttpGet("merged")]
    public async Task<ActionResult<NavDto>> GetMergedNav()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest("Job ID could not be determined from user token");

        var roleName = User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(roleName))
            return BadRequest("Role not found in token");

        if (!RoleNameToIdMap.TryGetValue(roleName, out var roleId))
            return BadRequest($"Unknown role: {roleName}");

        var nav = await _navRepo.GetMergedNavAsync(roleId, jobId.Value);
        if (nav == null)
            return Ok(new NavDto
            {
                NavId = 0,
                RoleId = roleId,
                JobId = jobId,
                Active = false,
                Items = new List<NavItemDto>()
            });

        return Ok(nav);
    }

    // ─── SuperUser editor endpoints ─────────────────────────────────

    /// <summary>Get all platform default navs with items.</summary>
    [HttpGet("editor/defaults")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<List<NavEditorNavDto>>> GetDefaults()
    {
        var defaults = await _navEditorService.GetAllDefaultsAsync();
        return Ok(defaults);
    }

    /// <summary>Get legacy menus for the current job (for import panel).</summary>
    [HttpGet("editor/legacy-menus")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<List<NavEditorLegacyMenuDto>>> GetLegacyMenus()
    {
        var jobId = await User.GetJobIdFromRegistrationAsync(_jobLookupService);
        if (jobId == null)
            return BadRequest("Job ID could not be determined from user token");

        var menus = await _navEditorService.GetLegacyMenusAsync(jobId.Value);
        return Ok(menus);
    }

    /// <summary>Create a platform default nav or job override.</summary>
    [HttpPost("editor/defaults")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<NavEditorNavDto>> CreateNav([FromBody] CreateNavRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var nav = await _navEditorService.CreateNavAsync(request, userId);
        return Ok(nav);
    }

    /// <summary>Create a new nav item.</summary>
    [HttpPost("editor/items")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<NavEditorNavItemDto>> CreateNavItem([FromBody] CreateNavItemRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var item = await _navEditorService.CreateNavItemAsync(request, userId);
        return Ok(item);
    }

    /// <summary>Update an existing nav item.</summary>
    [HttpPut("editor/items/{navItemId:int}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<NavEditorNavItemDto>> UpdateNavItem(int navItemId, [FromBody] UpdateNavItemRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var item = await _navEditorService.UpdateNavItemAsync(navItemId, request, userId);
        return Ok(item);
    }

    /// <summary>Delete a nav item.</summary>
    [HttpDelete("editor/items/{navItemId:int}")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> DeleteNavItem(int navItemId)
    {
        await _navEditorService.DeleteNavItemAsync(navItemId);
        return NoContent();
    }

    /// <summary>Reorder sibling nav items.</summary>
    [HttpPut("editor/items/reorder")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> ReorderNavItems([FromBody] ReorderNavItemsRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        await _navEditorService.ReorderNavItemsAsync(request, userId);
        return NoContent();
    }

    /// <summary>Import legacy menu items into the new nav structure.</summary>
    [HttpPost("editor/import-legacy")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<NavEditorNavDto>> ImportLegacy([FromBody] ImportLegacyMenuRequest request)
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var nav = await _navEditorService.ImportLegacyMenuAsync(request, userId);
        return Ok(nav);
    }

    /// <summary>Toggle the Active state of a platform default nav.</summary>
    [HttpPut("editor/defaults/{navId:int}/active")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<IActionResult> ToggleNavActive(int navId, [FromBody] ToggleNavActiveRequest request)
    {
        await _navEditorService.ToggleNavActiveAsync(navId, request.Active);
        return NoContent();
    }

    /// <summary>Ensure platform default navs exist for all standard roles.</summary>
    [HttpPost("editor/defaults/ensure-all-roles")]
    [Authorize(Policy = "SuperUserOnly")]
    public async Task<ActionResult<object>> EnsureAllRoleNavs()
    {
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User ID not found in token");

        var created = await _navEditorService.EnsureAllRoleNavsAsync(userId);
        return Ok(new { created });
    }
}
