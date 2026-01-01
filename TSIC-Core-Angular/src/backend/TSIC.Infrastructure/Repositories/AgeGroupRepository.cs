using Microsoft.EntityFrameworkCore;
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

    public async Task<(decimal? TeamFee, decimal? RosterFee)?> GetFeeInfoAsync(Guid ageGroupId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.AgegroupId == ageGroupId)
            .Select(a => new { a.TeamFee, a.RosterFee })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null ? (result.TeamFee, result.RosterFee) : null;
    }

    public IQueryable<Agegroups> Query()
    {
        return _context.Agegroups.AsQueryable();
    }
}
