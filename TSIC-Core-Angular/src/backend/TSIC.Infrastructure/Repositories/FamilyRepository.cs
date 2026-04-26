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

    public async Task<List<string>> GetFamilyPlayerEmailsForJobAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.User != null && r.User.Email != null)
            .Select(r => r.User!.Email!)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetFamilyPlayerEmailsForJobAsync(
        string jobPath,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.Job.JobPath == jobPath && r.FamilyUserId == familyUserId && r.User != null && r.User.Email != null)
            .Select(r => r.User!.Email!)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsPlayerInFamilyAsync(
        string callerUserId,
        string playerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(callerUserId) || string.IsNullOrWhiteSpace(playerUserId))
            return false;

        // Self always passes — a player is "in" their own family.
        if (string.Equals(callerUserId, playerUserId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Find the family-account holder for the player. Holder = FamilyUserId
        // when the player IS the holder; otherwise FamilyUserId from the
        // FamilyMembers row that has this player as FamilyMemberUserId.
        var playerFamilyUserId = await _context.Families
            .AsNoTracking()
            .Where(f => f.FamilyUserId == playerUserId)
            .Select(f => f.FamilyUserId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _context.FamilyMembers
                .AsNoTracking()
                .Where(fm => fm.FamilyMemberUserId == playerUserId)
                .Select(fm => fm.FamilyUserId)
                .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(playerFamilyUserId)) return false;

        // Caller is in the same family if they ARE the holder OR they appear
        // on a FamilyMembers row under the same FamilyUserId.
        if (string.Equals(callerUserId, playerFamilyUserId, StringComparison.OrdinalIgnoreCase))
            return true;

        return await _context.FamilyMembers
            .AsNoTracking()
            .AnyAsync(
                fm => fm.FamilyUserId == playerFamilyUserId
                   && fm.FamilyMemberUserId == callerUserId,
                cancellationToken);
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
