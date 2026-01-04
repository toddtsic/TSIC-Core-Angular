using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IRegistrationRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for Registrations entity and role-type queries.
/// This keeps data access logic centralized and services focused on business logic.
/// </summary>
public class RegistrationRepository : IRegistrationRepository
{
    private readonly SqlDbContext _context;

    public RegistrationRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<Registrations>> GetByUserIdAsync(
        string userId,
        bool includeJob = false,
        bool includeJobDisplayOptions = false,
        bool includeRole = false,
        string? roleIdFilter = null,
        string? roleNameFilter = null,
        bool activeOnly = false,
        bool nonExpiredOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Registrations.AsQueryable();

        // Apply filters
        query = query.Where(r => r.UserId == userId);

        if (activeOnly)
        {
            query = query.Where(r => r.BActive == true);
        }

        if (roleIdFilter != null)
        {
            query = query.Where(r => r.RoleId == roleIdFilter);
        }

        if (roleNameFilter != null)
        {
            query = query.Where(r => r.Role!.Name == roleNameFilter);
        }

        if (nonExpiredOnly)
        {
            var now = DateTime.Now;
            query = query.Where(r => now < r.Job!.ExpiryAdmin);
        }

        // Apply includes
        if (includeJob)
        {
            query = query.Include(r => r.Job);
        }

        if (includeJobDisplayOptions)
        {
            query = query.Include(r => r.Job!.JobDisplayOptions);
        }

        if (includeRole)
        {
            query = query.Include(r => r.Role);
        }

        return await query
            .AsNoTracking()
            .OrderBy(r => r.Job!.JobName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetByFamilyAndJobAsync(
        Guid jobId,
        string familyUserId,
        bool activePlayersOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Registrations
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId);

        if (activePlayersOnly)
        {
            query = query.Where(r => r.UserId != null);
        }

        return await query
            .OrderByDescending(r => r.Modified)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetByIdsAsync(
        List<Guid> registrationIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => registrationIds.Contains(r.RegistrationId))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetSuperUserRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryAdmin
                && role.Id == RoleConstants.Superuser
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                j.JobName ?? string.Empty,
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetSuperDirectorRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryAdmin
                && role.Name == "SuperDirector"
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                j.JobName ?? string.Empty,
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetDirectorRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryAdmin
                && role.Id == RoleConstants.Director
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                j.JobName ?? string.Empty,
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetPlayerRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
            join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.FamilyUserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryUsers
                && role.Id == RoleConstants.Player
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                $"{(j.JobName ?? string.Empty)}:{u.FirstName} {u.LastName}:{ag.AgegroupName}:{t.TeamName}",
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetClubRepRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryUsers
                && role.Id == RoleConstants.ClubRep
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                j.JobName ?? string.Empty,
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetStaffRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
            join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryUsers
                && role.Id == RoleConstants.Staff
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                $"{(j.JobName ?? string.Empty)}:{ag.AgegroupName}:{t.TeamName}",
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetStoreAdminRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryUsers
                && r.RoleId == RoleConstants.StoreAdmin
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                j.JobName ?? string.Empty,
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetRefAssignorRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryUsers
                && r.RoleId == RoleConstants.RefAssignor
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                j.JobName ?? string.Empty,
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationDto>> GetRefereeRegistrationsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId
            where
                (r.UserId == userId)
                && (r.BActive == true)
                && DateTime.Now < j.ExpiryUsers
                && r.RoleId == RoleConstants.Referee
            orderby j.JobName
            select new RegistrationDto(
                r.RegistrationId.ToString(),
                j.JobName ?? string.Empty,
                $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                j.JobPath
            )
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public void Add(Registrations registration)
    {
        _context.Registrations.Add(registration);
    }

    public void Update(Registrations registration)
    {
        _context.Registrations.Update(registration);
    }

    public void Remove(Registrations registration)
    {
        _context.Registrations.Remove(registration);
    }

    public IQueryable<Registrations> Query()
    {
        return _context.Registrations.AsQueryable();
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, int>> GetActiveTeamRosterCountsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.JobId == jobId && r.AssignedTeamId.HasValue && teamIds.Contains(r.AssignedTeamId.Value) && r.BActive == true)
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);
    }

    public async Task<List<Registrations>> GetFamilyRegistrationsForPlayersAsync(
        Guid jobId,
        string familyUserId,
        IReadOnlyCollection<string> playerIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.UserId != null && playerIds.Contains(r.UserId))
            .Include(r => r.AssignedTeam)
            .OrderByDescending(r => r.Modified)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetByJobAndFamilyWithUsersAsync(
        Guid jobId,
        string familyUserId,
        bool activePlayersOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Registrations
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId);

        if (activePlayersOnly)
        {
            query = query.Where(r => r.UserId != null);
        }

        // IMPORTANT: Remove AsNoTracking() so entities can be modified and persisted
        return await query
            .Include(r => r.User)
            .ToListAsync(cancellationToken);
    }

    public async Task<RegistrationWithInvoiceData?> GetRegistrationWithInvoiceDataAsync(
        Guid registrationId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.RegistrationId == registrationId && r.JobId == jobId)
            .Join(_context.Jobs, r => r.JobId, j => j.JobId, (r, j) => new { r, j })
            .Join(_context.Customers, x => x.j.CustomerId, c => c.CustomerId, (x, c) => new { x.r, x.j, c })
            .Select(x => new RegistrationWithInvoiceData(x.c.CustomerAi, x.j.JobAi, x.r.RegistrationAi))
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Dictionary<Guid, int>> GetRosterCountsByTeamAsync(
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.AssignedTeamId != null && teamIds.Contains(r.AssignedTeamId.Value))
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);
    }

    public async Task<List<EligibleInsuranceRegistration>> GetEligibleInsuranceRegistrationsAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.Now.AddHours(24);
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.FamilyUserId == familyUserId
                && r.FeeTotal > 0
                && r.RegsaverPolicyId == null
                && r.AssignedTeam != null
                && r.AssignedTeam.Expireondate > cutoff)
            .Select(r => new EligibleInsuranceRegistration(
                r.RegistrationId,
                r.AssignedTeamId!.Value,
                r.Assignment,
                r.User != null ? r.User.FirstName : null,
                r.User != null ? r.User.LastName : null,
                r.AssignedTeam != null ? r.AssignedTeam.PerRegistrantFee : null,
                (r.AssignedTeam != null && r.AssignedTeam.Agegroup != null) ? r.AssignedTeam.Agegroup.TeamFee : null,
                r.FeeTotal))
            .ToListAsync(cancellationToken);
    }

    public async Task<DirectorContactInfo?> GetDirectorContactForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.Role != null
                && r.Role.Name == "Director"
                && r.BActive == true)
            .OrderBy(r => r.RegistrationTs)
            .Select(r => new DirectorContactInfo(
                r.User != null ? r.User.Email : null,
                r.User != null ? r.User.FirstName : null,
                r.User != null ? r.User.LastName : null,
                r.User != null ? r.User.Cellphone : null,
                r.Job != null ? r.Job.JobName : null,
                r.Job != null && (r.Job.AdnArb == true)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Registrations>> ValidateRegistrationsForInsuranceAsync(
        Guid jobId,
        string familyUserId,
        IReadOnlyCollection<Guid> registrationIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.JobId == jobId
                && r.FamilyUserId == familyUserId
                && registrationIds.Contains(r.RegistrationId))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetByJobAndFamilyUserIdAsync(
        Guid jobId,
        string familyUserId,
        bool activePlayersOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Registrations
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId);

        if (activePlayersOnly)
        {
            query = query.Where(r => r.UserId != null);
        }

        return await query
            .OrderByDescending(r => r.Modified)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<RegistrationConfirmationData>> GetConfirmationDataAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId)
            .Select(r => new RegistrationConfirmationData(
                r.RegistrationId,
                (r.User != null ? r.User.FirstName : string.Empty) ?? string.Empty,
                (r.User != null ? r.User.LastName : string.Empty) ?? string.Empty,
                (r.AssignedTeam != null ? r.AssignedTeam.TeamName : string.Empty) ?? string.Empty,
                r.FeeTotal,
                r.PaidTotal,
                r.OwedTotal,
                r.RegsaverPolicyId,
                r.RegsaverPolicyIdCreateDate,
                r.AdnSubscriptionId,
                r.AdnSubscriptionStatus,
                r.AdnSubscriptionStartDate,
                r.AdnSubscriptionIntervalLength,
                r.AdnSubscriptionBillingOccurences,
                r.AdnSubscriptionAmountPerOccurence))
            .ToListAsync(cancellationToken);
    }
}
