using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for administrator registration data access.
/// </summary>
public class AdministratorRepository : IAdministratorRepository
{
    private readonly SqlDbContext _context;

    /// <summary>
    /// The set of role IDs that constitute administrative roles.
    /// </summary>
    private static readonly string[] AdminRoleIds =
    [
        RoleConstants.Superuser,
        RoleConstants.Director,
        RoleConstants.SuperDirector,
        RoleConstants.ApiAuthorized,
        RoleConstants.RefAssignor,
        RoleConstants.StoreAdmin,
        RoleConstants.StpAdmin
    ];

    /// <summary>
    /// The subset of admin roles eligible for batch status updates.
    /// Matches legacy behavior: excludes Superuser, StoreAdmin, StpAdmin, RefAssignor.
    /// </summary>
    private static readonly string[] BatchUpdatableRoleIds =
    [
        RoleConstants.Director,
        RoleConstants.SuperDirector,
        RoleConstants.ApiAuthorized
    ];

    public AdministratorRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<Registrations>> GetByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Include(r => r.User)
            .Include(r => r.Role)
            .Where(r => r.JobId == jobId && AdminRoleIds.Contains(r.RoleId!))
            .OrderBy(r => r.User!.LastName)
            .ThenBy(r => r.User!.FirstName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Registrations?> GetByIdAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Include(r => r.User)
            .Include(r => r.Role)
            .Where(r => r.RegistrationId == registrationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetBatchUpdatableByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.JobId == jobId && BatchUpdatableRoleIds.Contains(r.RoleId!))
            .ToListAsync(cancellationToken);
    }

    public void Add(Registrations registration)
    {
        _context.Registrations.Add(registration);
    }

    public void Remove(Registrations registration)
    {
        _context.Registrations.Remove(registration);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
