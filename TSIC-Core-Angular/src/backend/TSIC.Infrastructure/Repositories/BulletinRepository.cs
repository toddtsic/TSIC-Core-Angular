using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Bulletin;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
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

    public async Task<List<BulletinAdminDto>> GetAllBulletinsForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Bulletins
            .AsNoTracking()
            .Where(b => b.JobId == jobId)
            .OrderByDescending(b => b.CreateDate)
            .Select(b => new BulletinAdminDto
            {
                BulletinId = b.BulletinId,
                Title = b.Title,
                Text = b.Text,
                Active = b.Active,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                CreateDate = b.CreateDate,
                Modified = b.Modified,
                ModifiedByUsername = b.LebUser != null
                    ? b.LebUser.UserName
                    : null
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Bulletins?> GetByIdAsync(
        Guid bulletinId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Bulletins
            .FirstOrDefaultAsync(b => b.BulletinId == bulletinId, cancellationToken);
    }

    public async Task<int> BatchUpdateActiveStatusAsync(
        Guid jobId,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var bulletins = await _context.Bulletins
            .Where(b => b.JobId == jobId)
            .ToListAsync(cancellationToken);

        foreach (var b in bulletins)
        {
            b.Active = active;
        }

        return await _context.SaveChangesAsync(cancellationToken);
    }

    public void Add(Bulletins bulletin)
    {
        _context.Bulletins.Add(bulletin);
    }

    public void Remove(Bulletins bulletin)
    {
        _context.Bulletins.Remove(bulletin);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
