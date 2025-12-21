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

    public void Add(FamilyMembers familyMember)
    {
        _context.FamilyMembers.Add(familyMember);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
