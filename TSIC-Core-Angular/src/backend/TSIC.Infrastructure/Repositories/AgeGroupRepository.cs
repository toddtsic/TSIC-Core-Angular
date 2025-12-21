using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Agegroups entity using Entity Framework Core.
/// </summary>
public class AgeGroupRepository : IAgeGroupRepository
{
    private readonly SqlDbContext _context;

    public AgeGroupRepository(SqlDbContext context)
    {
        _context = context;
    }

    public IQueryable<Agegroups> Query()
    {
        return _context.Agegroups.AsQueryable();
    }
}
