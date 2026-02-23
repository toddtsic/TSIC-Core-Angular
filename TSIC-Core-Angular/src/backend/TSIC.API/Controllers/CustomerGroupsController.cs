using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TSIC.Contracts.Dtos.Customer;
using TSIC.Contracts.Services;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/customer-groups")]
[Authorize(Policy = "SuperUserOnly")]
public class CustomerGroupsController : ControllerBase
{
    private readonly ICustomerGroupService _service;

    public CustomerGroupsController(ICustomerGroupService service)
    {
        _service = service;
    }

    // ── Groups ───────────────────────────────────────────

    /// <summary>
    /// List all customer groups with member counts.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<CustomerGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerGroupDto>>> GetGroups(
        CancellationToken ct)
    {
        var groups = await _service.GetAllGroupsAsync(ct);
        return Ok(groups);
    }

    /// <summary>
    /// Create a new customer group.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CustomerGroupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerGroupDto>> CreateGroup(
        [FromBody] CreateCustomerGroupRequest request,
        CancellationToken ct)
    {
        try
        {
            var group = await _service.CreateGroupAsync(request, ct);
            return CreatedAtAction(nameof(GetGroups), new { }, group);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Rename a customer group.
    /// </summary>
    [HttpPut("{groupId:int}")]
    [ProducesResponseType(typeof(CustomerGroupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerGroupDto>> RenameGroup(
        int groupId,
        [FromBody] RenameCustomerGroupRequest request,
        CancellationToken ct)
    {
        try
        {
            var group = await _service.RenameGroupAsync(groupId, request, ct);
            return Ok(group);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a customer group (must have zero members).
    /// </summary>
    [HttpDelete("{groupId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteGroup(
        int groupId,
        CancellationToken ct)
    {
        try
        {
            await _service.DeleteGroupAsync(groupId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // ── Members ──────────────────────────────────────────

    /// <summary>
    /// List all members of a customer group.
    /// </summary>
    [HttpGet("{groupId:int}/members")]
    [ProducesResponseType(typeof(List<CustomerGroupMemberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerGroupMemberDto>>> GetMembers(
        int groupId,
        CancellationToken ct)
    {
        var members = await _service.GetMembersAsync(groupId, ct);
        return Ok(members);
    }

    /// <summary>
    /// Add a customer to a group.
    /// </summary>
    [HttpPost("{groupId:int}/members")]
    [ProducesResponseType(typeof(CustomerGroupMemberDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerGroupMemberDto>> AddMember(
        int groupId,
        [FromBody] AddCustomerGroupMemberRequest request,
        CancellationToken ct)
    {
        try
        {
            var member = await _service.AddMemberAsync(groupId, request, ct);
            return CreatedAtAction(nameof(GetMembers), new { groupId }, member);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Remove a member from a group.
    /// </summary>
    [HttpDelete("{groupId:int}/members/{memberId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveMember(
        int groupId,
        int memberId,
        CancellationToken ct)
    {
        try
        {
            await _service.RemoveMemberAsync(groupId, memberId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // ── Lookup ───────────────────────────────────────────

    /// <summary>
    /// Get customers not yet assigned to this group (for add-member dropdown).
    /// </summary>
    [HttpGet("{groupId:int}/available-customers")]
    [ProducesResponseType(typeof(List<CustomerLookupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CustomerLookupDto>>> GetAvailableCustomers(
        int groupId,
        CancellationToken ct)
    {
        var customers = await _service.GetAvailableCustomersAsync(groupId, ct);
        return Ok(customers);
    }
}
