using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Clubs entity using Entity Framework Core.
/// </summary>
public class ClubRepository : IClubRepository
{
    private readonly SqlDbContext _context;

    public ClubRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<Clubs?> GetByIdAsync(
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Clubs
            .Where(c => c.ClubId == clubId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Clubs?> GetByNameAsync(
        string clubName,
        CancellationToken cancellationToken = default)
    {
        return await _context.Clubs
            .Where(c => c.ClubName == clubName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ClubSearchCandidate>> GetSearchCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Clubs
            .Select(c => new ClubSearchCandidate
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName!,
                State = c.LebUser!.State,
                TeamCount = c.ClubTeams.Count
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ClubSearchCandidate>> GetSearchCandidatesAsync(
        string? state,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Clubs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(c => c.LebUser!.State == state);
        }

        return await query
            .Select(c => new ClubSearchCandidate
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName!,
                State = c.LebUser!.State,
                TeamCount = c.ClubTeams.Count
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public void Add(Clubs club)
    {
        _context.Clubs.Add(club);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
