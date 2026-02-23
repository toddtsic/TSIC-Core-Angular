using TSIC.Contracts.Dtos.Customer;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for customer group data access (SuperUser-only feature).
/// </summary>
public interface ICustomerGroupRepository
{
    // ── Read ─────────────────────────────────────────────

    /// <summary>
    /// Get all customer groups with member counts (AsNoTracking).
    /// </summary>
    Task<List<CustomerGroupDto>> GetAllGroupsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all members of a group with customer names (AsNoTracking).
    /// </summary>
    Task<List<CustomerGroupMemberDto>> GetMembersAsync(int groupId, CancellationToken ct = default);

    /// <summary>
    /// Get all customers for the add-member dropdown (AsNoTracking).
    /// </summary>
    Task<List<CustomerLookupDto>> GetAllCustomersAsync(CancellationToken ct = default);

    // ── Validation ───────────────────────────────────────

    /// <summary>
    /// Check if a group exists by ID.
    /// </summary>
    Task<bool> GroupExistsAsync(int groupId, CancellationToken ct = default);

    /// <summary>
    /// Check if a group name already exists (case-insensitive).
    /// Optionally exclude a group ID (for rename scenarios).
    /// </summary>
    Task<bool> GroupNameExistsAsync(string name, int? excludeGroupId = null, CancellationToken ct = default);

    /// <summary>
    /// Check if a customer is already a member of a group.
    /// </summary>
    Task<bool> MemberExistsAsync(int groupId, Guid customerId, CancellationToken ct = default);

    /// <summary>
    /// Get the number of members in a group.
    /// </summary>
    Task<int> GetMemberCountAsync(int groupId, CancellationToken ct = default);

    // ── Write ────────────────────────────────────────────

    void AddGroup(CustomerGroups group);

    /// <summary>
    /// Get a group by ID (tracked for mutation).
    /// </summary>
    Task<CustomerGroups?> GetGroupTrackedAsync(int groupId, CancellationToken ct = default);

    void RemoveGroup(CustomerGroups group);

    void AddMember(CustomerGroupCustomers member);

    /// <summary>
    /// Get a member by ID (tracked for mutation).
    /// </summary>
    Task<CustomerGroupCustomers?> GetMemberTrackedAsync(int memberId, CancellationToken ct = default);

    void RemoveMember(CustomerGroupCustomers member);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
