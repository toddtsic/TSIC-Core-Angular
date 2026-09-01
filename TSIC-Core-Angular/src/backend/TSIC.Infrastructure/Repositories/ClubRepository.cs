using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
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

    public async Task<List<ClubAffectedJob>> GetJobsWithTeamsForClubAsync(
        int clubId, CancellationToken cancellationToken = default)
    {
        return await (
            from t in _context.Teams
            join cte in _context.ClubTeams on t.ClubTeamId equals cte.ClubTeamId
            where cte.ClubId == clubId
            join j in _context.Jobs on t.JobId equals j.JobId
            group j by new { t.JobId, j.JobName } into g
            orderby g.Key.JobName
            select new ClubAffectedJob
            {
                JobId = g.Key.JobId,
                JobName = g.Key.JobName ?? string.Empty,
                TeamCount = g.Count()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
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
        // ClubName is not unique (634 clubs, no unique index — same-named clubs exist and are
        // legitimately distinct programs). Without an ORDER BY, "first" is whatever the engine
        // hands back, so the SAME rep could land on a DIFFERENT club between two page loads.
        // Order by ClubId so an unavoidable tie is at least stable. Callers that can do better
        // than a tie — the club-rep wizard resolves by MEMBERSHIP first — should.
        return await _context.Clubs
            .Where(c => c.ClubName == clubName)
            .OrderBy(c => c.ClubId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Clubs>> GetAllByNameAsync(
        string clubName,
        CancellationToken cancellationToken = default)
    {
        return await _context.Clubs
            .Where(c => c.ClubName == clubName)
            .OrderBy(c => c.ClubId)
            .ToListAsync(cancellationToken);
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
