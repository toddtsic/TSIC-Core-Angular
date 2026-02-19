using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
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

    public AdministratorRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdministratorDto>> GetByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var primaryContactId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.PrimaryContactRegistrationId)
            .FirstOrDefaultAsync(cancellationToken);

        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && AdminRoleIds.Contains(r.RoleId!))
            .OrderBy(r => r.User!.LastName)
            .ThenBy(r => r.User!.FirstName)
            .Select(r => new AdministratorDto
            {
                RegistrationId = r.RegistrationId,
                AdministratorName = ((r.User!.LastName ?? "") + ", " + (r.User.FirstName ?? "")).Trim(' ', ','),
                UserName = r.User.UserName ?? "",
                RoleName = r.RoleId == RoleConstants.Superuser ? null : r.Role!.Name,
                IsActive = r.BActive ?? false,
                RegisteredDate = r.RegistrationTs,
                IsSuperuser = r.RoleId == RoleConstants.Superuser,
                IsPrimaryContact = r.RegistrationId == primaryContactId
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Registrations?> GetByIdAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations.FindAsync([registrationId], cancellationToken);
    }

    public async Task<AdministratorDto?> GetAdminProjectionByIdAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new AdministratorDto
            {
                RegistrationId = r.RegistrationId,
                AdministratorName = ((r.User!.LastName ?? "") + ", " + (r.User.FirstName ?? "")).Trim(' ', ','),
                UserName = r.User.UserName ?? "",
                RoleName = r.RoleId == RoleConstants.Superuser ? null : r.Role!.Name,
                IsActive = r.BActive ?? false,
                RegisteredDate = r.RegistrationTs,
                IsSuperuser = r.RoleId == RoleConstants.Superuser,
                IsPrimaryContact = r.Job!.PrimaryContactRegistrationId == r.RegistrationId
            })
            .FirstOrDefaultAsync(cancellationToken);
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

    public async Task<List<Registrations>> GetNonSuperuserByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.JobId == jobId
                && AdminRoleIds.Contains(r.RoleId!)
                && r.RoleId != RoleConstants.Superuser)
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid?> GetPrimaryContactIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.PrimaryContactRegistrationId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SetPrimaryContactAsync(
        Guid jobId,
        Guid? registrationId,
        CancellationToken cancellationToken = default)
    {
        var job = await _context.Jobs.FindAsync([jobId], cancellationToken)
            ?? throw new KeyNotFoundException($"Job '{jobId}' not found.");

        job.PrimaryContactRegistrationId = registrationId;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
