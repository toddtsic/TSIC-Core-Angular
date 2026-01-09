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
        // Need to do this as a group join since we can't directly access ClubReps from Registrations
        var clubs = await _context.Clubs
            .Select(c => new
            {
                c.ClubId,
                c.ClubName,
                State = c.LebUser!.State
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = new List<ClubSearchCandidate>();
        foreach (var c in clubs)
        {
            var teamCount = await _context.ClubReps
                .Where(cr => cr.ClubId == c.ClubId)
                .Join(_context.Registrations,
                    cr => cr.ClubRepUserId,
                    reg => reg.UserId,
                    (cr, reg) => reg.RegistrationId)
                .Join(_context.Teams,
                    regId => regId,
                    team => team.ClubrepRegistrationid,
                    (regId, team) => team)
                .CountAsync(cancellationToken);

            result.Add(new ClubSearchCandidate
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName!,
                State = c.State,
                TeamCount = teamCount
            });
        }

        return result;
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

        var clubs = await query
            .Select(c => new
            {
                c.ClubId,
                c.ClubName,
                State = c.LebUser!.State
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var result = new List<ClubSearchCandidate>();
        foreach (var c in clubs)
        {
            var teamCount = await _context.ClubReps
                .Where(cr => cr.ClubId == c.ClubId)
                .Join(_context.Registrations,
                    cr => cr.ClubRepUserId,
                    reg => reg.UserId,
                    (cr, reg) => reg.RegistrationId)
                .Join(_context.Teams,
                    regId => regId,
                    team => team.ClubrepRegistrationid,
                    (regId, team) => team)
                .CountAsync(cancellationToken);

            result.Add(new ClubSearchCandidate
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName!,
                State = c.State,
                TeamCount = teamCount
            });
        }

        return result;
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
