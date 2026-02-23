using TSIC.Contracts.Dtos.Customer;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing customer groups (SuperUser-only feature).
/// </summary>
public interface ICustomerGroupService
{
    // ── Groups ───────────────────────────────────────────

    Task<List<CustomerGroupDto>> GetAllGroupsAsync(CancellationToken ct = default);

    Task<CustomerGroupDto> CreateGroupAsync(CreateCustomerGroupRequest request, CancellationToken ct = default);

    Task<CustomerGroupDto> RenameGroupAsync(int groupId, RenameCustomerGroupRequest request, CancellationToken ct = default);

    Task DeleteGroupAsync(int groupId, CancellationToken ct = default);

    // ── Members ──────────────────────────────────────────

    Task<List<CustomerGroupMemberDto>> GetMembersAsync(int groupId, CancellationToken ct = default);

    Task<CustomerGroupMemberDto> AddMemberAsync(int groupId, AddCustomerGroupMemberRequest request, CancellationToken ct = default);

    Task RemoveMemberAsync(int groupId, int memberId, CancellationToken ct = default);

    // ── Lookup ───────────────────────────────────────────

    Task<List<CustomerLookupDto>> GetAvailableCustomersAsync(int groupId, CancellationToken ct = default);
}
