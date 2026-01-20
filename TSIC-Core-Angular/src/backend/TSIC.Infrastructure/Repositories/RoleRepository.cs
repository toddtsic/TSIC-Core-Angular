using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for AspNetRoles entity using Entity Framework Core.
/// </summary>
public class RoleRepository : IRoleRepository
{
    private readonly SqlDbContext _context;

    public RoleRepository(SqlDbContext context)
    {
        _context = context;
    }

}
