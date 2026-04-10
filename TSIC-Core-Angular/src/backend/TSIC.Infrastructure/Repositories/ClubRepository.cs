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

    /// <summary>
    /// Single-query approach: gets all clubs with team counts and primary rep contact.
    /// Replaces the old N+1 loop that issued one COUNT query per club.
    /// </summary>
    public async Task<List<ClubSearchCandidate>> GetSearchCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.Clubs
            .Select(c => new ClubSearchCandidate
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName!,
                State = c.ClubReps.OrderBy(cr => cr.Aid).Select(cr => cr.ClubRepUser.State).FirstOrDefault(),
                TeamCount = _context.ClubTeams
                    .Count(ct => ct.ClubId == c.ClubId),
                RepName = c.ClubReps.OrderBy(cr => cr.Aid)
                    .Select(cr => cr.ClubRepUser.FirstName != null && cr.ClubRepUser.LastName != null
                        ? cr.ClubRepUser.FirstName + " " + cr.ClubRepUser.LastName
                        : null)
                    .FirstOrDefault(),
                RepEmail = c.ClubReps.OrderBy(cr => cr.Aid)
                    .Select(cr => cr.ClubRepUser.Email)
                    .FirstOrDefault()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Single-query approach with optional state filter.
    /// </summary>
    public async Task<List<ClubSearchCandidate>> GetSearchCandidatesAsync(
        string? state,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Clubs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(c => c.ClubReps.Any(cr => cr.ClubRepUser.State == state));
        }

        return await query
            .Select(c => new ClubSearchCandidate
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName!,
                State = c.ClubReps.OrderBy(cr => cr.Aid).Select(cr => cr.ClubRepUser.State).FirstOrDefault(),
                TeamCount = _context.ClubTeams
                    .Count(ct => ct.ClubId == c.ClubId),
                RepName = c.ClubReps.OrderBy(cr => cr.Aid)
                    .Select(cr => cr.ClubRepUser.FirstName != null && cr.ClubRepUser.LastName != null
                        ? cr.ClubRepUser.FirstName + " " + cr.ClubRepUser.LastName
                        : null)
                    .FirstOrDefault(),
                RepEmail = c.ClubReps.OrderBy(cr => cr.Aid)
                    .Select(cr => cr.ClubRepUser.Email)
                    .FirstOrDefault()
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
