using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IUserRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for AspNetUsers entity.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly SqlDbContext _context;

    public UserRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<AspNetUsers?> GetByIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<bool> RequiresTosSignatureAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.UserName == username)
            .Select(u => new { u.BTsicwaiverSigned, u.TsicwaiverSignedTs })
            .SingleOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return true; // Require signature if user not found
        }

        // Require signature if never signed or if signature is more than 1 year old
        return !user.BTsicwaiverSigned ||
               user.TsicwaiverSignedTs == null ||
               user.TsicwaiverSignedTs.Value.AddYears(1) < DateTime.UtcNow;
    }

    public async Task UpdateTosAcceptanceAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .SingleOrDefaultAsync(u => u.UserName == username, cancellationToken);

        if (user != null)
        {
            user.BTsicwaiverSigned = true;
            user.TsicwaiverSignedTs = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateTosAcceptanceByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user != null)
        {
            user.BTsicwaiverSigned = true;
            user.TsicwaiverSignedTs = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public IQueryable<AspNetUsers> Query()
    {
        return _context.AspNetUsers.AsQueryable();
    }

    public async Task<Dictionary<string, UserNameInfo>> GetUserNameMapAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var data = await _context.AspNetUsers
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        return data.ToDictionary(x => x.Id, x => new UserNameInfo(x.FirstName, x.LastName));
    }

    public async Task<UserContactInfo?> GetUserContactInfoAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .Where(u => u.Id == userId)
            .Select(u => new UserContactInfo(
                u.FirstName,
                u.LastName,
                u.Email,
                u.StreetAddress,
                u.City,
                u.State,
                u.PostalCode,
                u.Cellphone,
                u.Phone
            ))
            .FirstOrDefaultAsync(cancellationToken);

        return user;
    }
}
