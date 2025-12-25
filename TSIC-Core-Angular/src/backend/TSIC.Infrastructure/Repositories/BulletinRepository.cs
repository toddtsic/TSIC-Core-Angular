using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IBulletinRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for Bulletins entity.
/// </summary>
public class BulletinRepository : IBulletinRepository
{
    private readonly SqlDbContext _context;

    public BulletinRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<BulletinDto>> GetActiveBulletinsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return await _context.Bulletins
            .AsNoTracking()
            .Where(b => b.JobId == jobId)
            .Where(b => b.Active == true)
            .Where(b => b.StartDate != null && b.StartDate <= now)
            .Where(b => b.EndDate == null || b.EndDate >= now)
            .OrderByDescending(b => b.StartDate)
            .Select(b => new BulletinDto
            {
                BulletinId = b.BulletinId,
                Title = b.Title,
                Text = b.Text,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                CreateDate = b.CreateDate
            })
            .ToListAsync(cancellationToken);
    }
}
