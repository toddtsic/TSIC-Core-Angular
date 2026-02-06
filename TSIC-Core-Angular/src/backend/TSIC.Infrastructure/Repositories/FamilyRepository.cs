using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IFamilyRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for Families entity.
/// </summary>
public class FamilyRepository : IFamilyRepository
{
    private readonly SqlDbContext _context;

    public FamilyRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<Families?> GetByFamilyUserIdAsync(
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Families
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.FamilyUserId == familyUserId, cancellationToken);
    }

    public async Task<List<Registrations>> GetFamilyRegistrationsForJobAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Include(r => r.User)
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetFamilyRegistrationsForJobAsync(
        string jobPath,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Job)
            .Where(r => r.Job.JobPath == jobPath && r.FamilyUserId == familyUserId)
            .ToListAsync(cancellationToken);
    }

    public async Task<FamilyContactInfo?> GetFamilyContactAsync(
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Families
            .AsNoTracking()
            .Where(f => f.FamilyUserId == familyUserId)
            .Select(f => new FamilyContactInfo
            {
                FirstName = f.MomFirstName,
                LastName = f.MomLastName,
                Email = f.MomEmail,
                Phone = f.MomCellphone,
                City = f.FamilyUser.City,
                State = f.FamilyUser.State,
                Zip = f.FamilyUser.PostalCode
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
}
