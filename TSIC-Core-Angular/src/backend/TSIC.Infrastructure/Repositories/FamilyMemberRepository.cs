using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class FamilyMemberRepository : IFamilyMemberRepository
{
    private readonly SqlDbContext _context;

    public FamilyMemberRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<string>> GetChildUserIdsAsync(string familyUserId, CancellationToken cancellationToken = default)
    {
        return await _context.FamilyMembers
            .AsNoTracking()
            .Where(fm => fm.FamilyUserId == familyUserId)
            .Select(fm => fm.FamilyMemberUserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> GetParentFamilyUserIdAsync(string childUserId, CancellationToken cancellationToken = default)
    {
        return await _context.FamilyMembers
            .AsNoTracking()
            .Where(fm => fm.FamilyMemberUserId == childUserId)
            .Select(fm => fm.FamilyUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(FamilyMembers familyMember)
    {
        _context.FamilyMembers.Add(familyMember);
    }

    public async Task<bool> RemoveByChildUserIdAsync(string familyUserId, string childUserId, CancellationToken cancellationToken = default)
    {
        var link = await _context.FamilyMembers
            .FirstOrDefaultAsync(fm => fm.FamilyUserId == familyUserId && fm.FamilyMemberUserId == childUserId, cancellationToken);
        if (link == null) return false;
        _context.FamilyMembers.Remove(link);
        return true;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
