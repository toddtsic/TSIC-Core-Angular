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

    public IQueryable<AspNetUsers> Query()
    {
        return _context.AspNetUsers.AsQueryable();
    }
}
