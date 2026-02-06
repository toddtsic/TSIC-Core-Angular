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

    public async Task<List<UserBasicInfo>> GetUsersByIdsAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserBasicInfo
            {
                UserId = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Birthdate = u.Dob
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, UserNameInfo>> GetUserNameMapAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var data = await _context.AspNetUsers
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        return data.ToDictionary(x => x.Id, x => new UserNameInfo
        {
            FirstName = x.FirstName,
            LastName = x.LastName
        });
    }

    public async Task<UserContactInfo?> GetUserContactInfoAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .Where(u => u.Id == userId)
            .Select(u => new UserContactInfo
            {
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode,
                Cellphone = u.Cellphone,
                Phone = u.Phone
            })
            .FirstOrDefaultAsync(cancellationToken);

        return user;
    }

    public async Task<List<AspNetUsers>> GetUsersForFamilyAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new List<AspNetUsers>();
        }

        return await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new AspNetUsers
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Gender = u.Gender,
                Dob = u.Dob,
                Email = u.Email,
                Cellphone = u.Cellphone,
                Phone = u.Phone,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<UserSearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var lowerQuery = query.ToLower();

        return await _context.AspNetUsers
            .AsNoTracking()
            .Where(u =>
                u.UserName!.ToLower().Contains(lowerQuery) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(lowerQuery)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(lowerQuery)))
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Take(maxResults)
            .Select(u => new UserSearchResult
            {
                UserId = u.Id,
                UserName = u.UserName!,
                FirstName = u.FirstName,
                LastName = u.LastName
            })
            .ToListAsync(cancellationToken);
    }
}
