using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Customer;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for customer group data access.
/// </summary>
public class CustomerGroupRepository : ICustomerGroupRepository
{
    private readonly SqlDbContext _context;

    public CustomerGroupRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Read ─────────────────────────────────────────────

    public async Task<List<CustomerGroupDto>> GetAllGroupsAsync(CancellationToken ct = default)
    {
        return await _context.CustomerGroups
            .AsNoTracking()
            .OrderBy(g => g.CustomerGroupName)
            .Select(g => new CustomerGroupDto
            {
                Id = g.Id,
                CustomerGroupName = g.CustomerGroupName,
                MemberCount = g.CustomerGroupCustomers.Count
            })
            .ToListAsync(ct);
    }

    public async Task<List<CustomerGroupMemberDto>> GetMembersAsync(int groupId, CancellationToken ct = default)
    {
        return await _context.CustomerGroupCustomers
            .AsNoTracking()
            .Where(cgc => cgc.CustomerGroupId == groupId)
            .OrderBy(cgc => cgc.Customer.CustomerName)
            .Select(cgc => new CustomerGroupMemberDto
            {
                Id = cgc.Id,
                CustomerGroupId = cgc.CustomerGroupId,
                CustomerId = cgc.CustomerId,
                CustomerName = cgc.Customer.CustomerName ?? ""
            })
            .ToListAsync(ct);
    }

    public async Task<List<CustomerLookupDto>> GetAllCustomersAsync(CancellationToken ct = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .OrderBy(c => c.CustomerName)
            .Select(c => new CustomerLookupDto
            {
                CustomerId = c.CustomerId,
                CustomerName = c.CustomerName ?? ""
            })
            .ToListAsync(ct);
    }

    // ── Validation ───────────────────────────────────────

    public async Task<bool> GroupExistsAsync(int groupId, CancellationToken ct = default)
    {
        return await _context.CustomerGroups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupId, ct);
    }

    public async Task<bool> GroupNameExistsAsync(string name, int? excludeGroupId = null, CancellationToken ct = default)
    {
        var query = _context.CustomerGroups.AsNoTracking()
            .Where(g => g.CustomerGroupName.ToLower() == name.ToLower());

        if (excludeGroupId.HasValue)
        {
            query = query.Where(g => g.Id != excludeGroupId.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<bool> MemberExistsAsync(int groupId, Guid customerId, CancellationToken ct = default)
    {
        return await _context.CustomerGroupCustomers
            .AsNoTracking()
            .AnyAsync(cgc => cgc.CustomerGroupId == groupId && cgc.CustomerId == customerId, ct);
    }

    public async Task<int> GetMemberCountAsync(int groupId, CancellationToken ct = default)
    {
        return await _context.CustomerGroupCustomers
            .AsNoTracking()
            .CountAsync(cgc => cgc.CustomerGroupId == groupId, ct);
    }

    // ── Write ────────────────────────────────────────────

    public void AddGroup(CustomerGroups group)
    {
        _context.CustomerGroups.Add(group);
    }

    public async Task<CustomerGroups?> GetGroupTrackedAsync(int groupId, CancellationToken ct = default)
    {
        return await _context.CustomerGroups.FindAsync([groupId], ct);
    }

    public void RemoveGroup(CustomerGroups group)
    {
        _context.CustomerGroups.Remove(group);
    }

    public void AddMember(CustomerGroupCustomers member)
    {
        _context.CustomerGroupCustomers.Add(member);
    }

    public async Task<CustomerGroupCustomers?> GetMemberTrackedAsync(int memberId, CancellationToken ct = default)
    {
        return await _context.CustomerGroupCustomers.FindAsync([memberId], ct);
    }

    public void RemoveMember(CustomerGroupCustomers member)
    {
        _context.CustomerGroupCustomers.Remove(member);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
