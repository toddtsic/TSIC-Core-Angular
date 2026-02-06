using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for ClubReps entity using Entity Framework Core.
/// </summary>
public class ClubRepRepository : IClubRepRepository
{
    private readonly SqlDbContext _context;

    public ClubRepRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ClubWithUsageInfo>> GetClubsForUserAsync(
        string clubRepUserId,
        CancellationToken cancellationToken = default)
    {
        var clubReps = await _context.ClubReps
            .Where(cr => cr.ClubRepUserId == clubRepUserId)
            .Include(cr => cr.Club)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = new List<ClubWithUsageInfo>();
        foreach (var cr in clubReps)
        {
            var clubName = cr.Club!.ClubName!;

            // Check if this specific club (by name) has any teams registered
            // Teams.ClubrepRegistrationid -> Registrations.ClubName
            var hasTeams = await _context.Teams
                .Where(t => t.ClubrepRegistrationid != null)
                .Join(_context.Registrations,
                    t => t.ClubrepRegistrationid,
                    r => r.RegistrationId,
                    (t, r) => r.ClubName)
                .AnyAsync(rcn => rcn == clubName, cancellationToken);

            result.Add(new ClubWithUsageInfo
            {
                ClubId = cr.ClubId,
                ClubName = clubName,
                IsInUse = hasTeams
            });
        }

        return result;
    }

    public async Task<ClubReps?> GetClubRepForUserAndClubAsync(
        string clubRepUserId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubReps
            .Where(cr => cr.ClubRepUserId == clubRepUserId && cr.ClubId == clubId)
            .Include(cr => cr.Club)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        string clubRepUserId,
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubReps
            .AnyAsync(cr => cr.ClubRepUserId == clubRepUserId && cr.ClubId == clubId, cancellationToken);
    }

    public void Add(ClubReps clubRep)
    {
        _context.ClubReps.Add(clubRep);
    }

    public void Remove(ClubReps clubRep)
    {
        _context.ClubReps.Remove(clubRep);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
