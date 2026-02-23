using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scoring;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for mobile scorer data access.
/// </summary>
public class MobileScorerRepository : IMobileScorerRepository
{
    private readonly SqlDbContext _context;

    public MobileScorerRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Read ─────────────────────────────────────────────

    public async Task<List<MobileScorerDto>> GetScorersForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.JobId == jobId
                  && r.RoleId == RoleConstants.Scorer
            orderby u.LastName, u.FirstName
            select new MobileScorerDto
            {
                RegistrationId = r.RegistrationId,
                Username = u.UserName ?? "",
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Cellphone = u.Cellphone,
                BActive = r.BActive ?? false
            }
        ).AsNoTracking().ToListAsync(ct);
    }

    public async Task<Registrations?> GetScorerRegistrationAsync(Guid registrationId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId
                                      && r.RoleId == RoleConstants.Scorer, ct);
    }

    public async Task<int> GetUserRegistrationCountAsync(string userId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .CountAsync(r => r.UserId == userId, ct);
    }

    // ── Write ────────────────────────────────────────────

    public void AddRegistration(Registrations registration)
    {
        _context.Registrations.Add(registration);
    }

    public void RemoveRegistration(Registrations registration)
    {
        _context.Registrations.Remove(registration);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
