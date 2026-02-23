using TSIC.Contracts.Dtos.Customer;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for managing customer groups with validation logic.
/// </summary>
public class CustomerGroupService : ICustomerGroupService
{
    private readonly ICustomerGroupRepository _repo;

    public CustomerGroupService(ICustomerGroupRepository repo)
    {
        _repo = repo;
    }

    // ── Groups ───────────────────────────────────────────

    public async Task<List<CustomerGroupDto>> GetAllGroupsAsync(CancellationToken ct = default)
    {
        return await _repo.GetAllGroupsAsync(ct);
    }

    public async Task<CustomerGroupDto> CreateGroupAsync(
        CreateCustomerGroupRequest request, CancellationToken ct = default)
    {
        var trimmed = request.CustomerGroupName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Group name is required.");

        if (await _repo.GroupNameExistsAsync(trimmed, ct: ct))
            throw new InvalidOperationException($"A group named '{trimmed}' already exists.");

        var entity = new CustomerGroups { CustomerGroupName = trimmed };
        _repo.AddGroup(entity);
        await _repo.SaveChangesAsync(ct);

        return new CustomerGroupDto
        {
            Id = entity.Id,
            CustomerGroupName = entity.CustomerGroupName,
            MemberCount = 0
        };
    }

    public async Task<CustomerGroupDto> RenameGroupAsync(
        int groupId, RenameCustomerGroupRequest request, CancellationToken ct = default)
    {
        var trimmed = request.CustomerGroupName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Group name is required.");

        var group = await _repo.GetGroupTrackedAsync(groupId, ct)
            ?? throw new KeyNotFoundException($"Group {groupId} not found.");

        if (await _repo.GroupNameExistsAsync(trimmed, excludeGroupId: groupId, ct: ct))
            throw new InvalidOperationException($"A group named '{trimmed}' already exists.");

        group.CustomerGroupName = trimmed;
        await _repo.SaveChangesAsync(ct);

        var memberCount = await _repo.GetMemberCountAsync(groupId, ct);
        return new CustomerGroupDto
        {
            Id = group.Id,
            CustomerGroupName = group.CustomerGroupName,
            MemberCount = memberCount
        };
    }

    public async Task DeleteGroupAsync(int groupId, CancellationToken ct = default)
    {
        var group = await _repo.GetGroupTrackedAsync(groupId, ct)
            ?? throw new KeyNotFoundException($"Group {groupId} not found.");

        var memberCount = await _repo.GetMemberCountAsync(groupId, ct);
        if (memberCount > 0)
            throw new InvalidOperationException(
                $"Cannot delete group '{group.CustomerGroupName}' — it has {memberCount} member(s). Remove all members first.");

        _repo.RemoveGroup(group);
        await _repo.SaveChangesAsync(ct);
    }

    // ── Members ──────────────────────────────────────────

    public async Task<List<CustomerGroupMemberDto>> GetMembersAsync(
        int groupId, CancellationToken ct = default)
    {
        return await _repo.GetMembersAsync(groupId, ct);
    }

    public async Task<CustomerGroupMemberDto> AddMemberAsync(
        int groupId, AddCustomerGroupMemberRequest request, CancellationToken ct = default)
    {
        if (!await _repo.GroupExistsAsync(groupId, ct))
            throw new KeyNotFoundException($"Group {groupId} not found.");

        if (await _repo.MemberExistsAsync(groupId, request.CustomerId, ct))
            throw new InvalidOperationException("This customer is already a member of this group.");

        var entity = new CustomerGroupCustomers
        {
            CustomerGroupId = groupId,
            CustomerId = request.CustomerId
        };
        _repo.AddMember(entity);
        await _repo.SaveChangesAsync(ct);

        // Re-query to get the full DTO with customer name
        var members = await _repo.GetMembersAsync(groupId, ct);
        return members.First(m => m.Id == entity.Id);
    }

    public async Task RemoveMemberAsync(int groupId, int memberId, CancellationToken ct = default)
    {
        var member = await _repo.GetMemberTrackedAsync(memberId, ct)
            ?? throw new KeyNotFoundException($"Member {memberId} not found.");

        if (member.CustomerGroupId != groupId)
            throw new KeyNotFoundException($"Member {memberId} does not belong to group {groupId}.");

        _repo.RemoveMember(member);
        await _repo.SaveChangesAsync(ct);
    }

    // ── Lookup ───────────────────────────────────────────

    public async Task<List<CustomerLookupDto>> GetAvailableCustomersAsync(
        int groupId, CancellationToken ct = default)
    {
        var allCustomers = await _repo.GetAllCustomersAsync(ct);
        var currentMembers = await _repo.GetMembersAsync(groupId, ct);
        var assignedIds = currentMembers.Select(m => m.CustomerId).ToHashSet();

        return allCustomers
            .Where(c => !assignedIds.Contains(c.CustomerId))
            .OrderBy(c => c.CustomerName)
            .ToList();
    }
}
