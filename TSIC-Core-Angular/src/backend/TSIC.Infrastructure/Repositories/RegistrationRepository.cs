using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Extensions;
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = j.JobName ?? string.Empty,
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = j.JobName ?? string.Empty,
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = j.JobName ?? string.Empty,
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = $"{(j.JobName ?? string.Empty)}:{u.FirstName} {u.LastName}:{ag.AgegroupName}:{t.TeamName}",
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = j.JobName ?? string.Empty,
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = $"{(j.JobName ?? string.Empty)}:{ag.AgegroupName}:{t.TeamName}",
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = j.JobName ?? string.Empty,
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = j.JobName ?? string.Empty,
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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
            select new RegistrationDto
            {
                RegId = r.RegistrationId.ToString(),
                DisplayText = j.JobName ?? string.Empty,
                JobLogo = $"{TsicConstants.BaseUrlStatics}BannerFiles/{jdo.LogoHeader}",
                JobPath = j.JobPath
            }
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

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAccountingRecordsAsync(Guid registrationId, CancellationToken ct = default)
        => await _context.RegistrationAccounting.AsNoTracking()
            .AnyAsync(a => a.RegistrationId == registrationId, ct);

    public async Task<bool> HasStoreCartBatchRecordsAsync(Guid registrationId, CancellationToken ct = default)
        => await _context.StoreCartBatchSkus.AsNoTracking()
            .AnyAsync(s => s.DirectToRegId == registrationId, ct);

    public async Task<string?> GetRegistrationRoleNameAsync(Guid registrationId, CancellationToken ct = default)
        => await _context.Registrations.AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => r.Role != null ? r.Role.Name : null)
            .FirstOrDefaultAsync(ct);

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
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.UserId != null && playerIds.Contains(r.UserId))
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
            .Select(x => new RegistrationWithInvoiceData
            {
                CustomerAi = x.c.CustomerAi,
                JobAi = x.j.JobAi,
                RegistrationAi = x.r.RegistrationAi
            })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> GetRegistrationJobIdAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => (Guid?)r.JobId)
            .FirstOrDefaultAsync(cancellationToken);
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
            .Select(r => new EligibleInsuranceRegistration
            {
                RegistrationId = r.RegistrationId,
                AssignedTeamId = r.AssignedTeamId!.Value,
                Assignment = r.Assignment,
                FirstName = r.User != null ? r.User.FirstName : null,
                LastName = r.User != null ? r.User.LastName : null,
                PerRegistrantFee = r.AssignedTeam != null ? r.AssignedTeam.PerRegistrantFee : null,
                TeamFee = (r.AssignedTeam != null && r.AssignedTeam.Agegroup != null) ? r.AssignedTeam.Agegroup.TeamFee : null,
                FeeTotal = r.FeeTotal
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<DirectorContactInfo?> GetDirectorContactForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Prefer explicitly-set primary contact
        var primaryContactId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.PrimaryContactRegistrationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (primaryContactId != null)
        {
            var explicitContact = await _context.Registrations
                .AsNoTracking()
                .Where(r => r.RegistrationId == primaryContactId && r.BActive == true)
                .Select(r => new DirectorContactInfo
                {
                    Email = r.User != null ? r.User.Email : null,
                    FirstName = r.User != null ? r.User.FirstName : null,
                    LastName = r.User != null ? r.User.LastName : null,
                    Cellphone = r.User != null ? r.User.Cellphone : null,
                    OrgName = r.Job != null ? r.Job.JobName : null,
                    PaymentPlan = r.Job != null && (r.Job.AdnArb == true)
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (explicitContact != null)
                return explicitContact;
        }

        // Fallback: earliest-registered active Director
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.Role != null
                && r.Role.Name == "Director"
                && r.BActive == true)
            .OrderBy(r => r.RegistrationTs)
            .Select(r => new DirectorContactInfo
            {
                Email = r.User != null ? r.User.Email : null,
                FirstName = r.User != null ? r.User.FirstName : null,
                LastName = r.User != null ? r.User.LastName : null,
                Cellphone = r.User != null ? r.User.Cellphone : null,
                OrgName = r.Job != null ? r.Job.JobName : null,
                PaymentPlan = r.Job != null && (r.Job.AdnArb == true)
            })
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
            .Select(r => new RegistrationConfirmationData
            {
                RegistrationId = r.RegistrationId,
                PlayerFirst = (r.User != null ? r.User.FirstName : string.Empty) ?? string.Empty,
                PlayerLast = (r.User != null ? r.User.LastName : string.Empty) ?? string.Empty,
                TeamName = (r.AssignedTeam != null ? r.AssignedTeam.TeamName : string.Empty) ?? string.Empty,
                FeeTotal = r.FeeTotal,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,
                RegsaverPolicyId = r.RegsaverPolicyId,
                RegsaverPolicyIdCreateDate = r.RegsaverPolicyIdCreateDate,
                AdnSubscriptionId = r.AdnSubscriptionId,
                AdnSubscriptionStatus = r.AdnSubscriptionStatus,
                AdnSubscriptionStartDate = r.AdnSubscriptionStartDate,
                AdnSubscriptionIntervalLength = r.AdnSubscriptionIntervalLength,
                AdnSubscriptionBillingOccurences = r.AdnSubscriptionBillingOccurences,
                AdnSubscriptionAmountPerOccurence = r.AdnSubscriptionAmountPerOccurence
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetRegistrationsByUserIdsAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new List<Registrations>();
        }

        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.UserId != null && userIds.Contains(r.UserId))
            .OrderByDescending(r => r.Modified)
            .ThenByDescending(r => r.RegistrationTs)
            .ToListAsync(cancellationToken);
    }

    public async Task<RegSaverPolicyInfo?> GetLatestRegSaverPolicyAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        var data = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.RegsaverPolicyId != null)
            .OrderByDescending(r => r.BActive == true)
            .ThenByDescending(r => r.RegsaverPolicyIdCreateDate)
            .Select(r => new { r.RegsaverPolicyId, r.RegsaverPolicyIdCreateDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (data?.RegsaverPolicyId == null)
        {
            return null;
        }

        return new RegSaverPolicyInfo
        {
            PolicyId = data.RegsaverPolicyId,
            PolicyCreateDate = data.RegsaverPolicyIdCreateDate
        };
    }

    public async Task<Registrations?> GetClubRepRegistrationAsync(string userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.UserId == userId && r.JobId == jobId && r.RoleId == Domain.Constants.RoleConstants.ClubRep)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RegistrationBasicInfo?> GetRegistrationBasicInfoAsync(Guid registrationId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId && r.UserId == userId)
            .Select(r => new RegistrationBasicInfo
            {
                ClubName = r.ClubName,
                JobId = r.JobId
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Registrations?> GetByIdAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations.FindAsync(registrationId);
    }

    public async Task<List<Registrations>> GetByJobAndUserIdsAsync(Guid jobId, List<string> userIds, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && userIds.Contains(r.UserId!))
            .OrderBy(r => r.Modified)
            .ThenBy(r => r.RegistrationTs)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetByJobAndFamilyUserIdAsync(
        Guid jobId,
        string familyUserId,
        string? regsaverPolicyId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId);

        if (regsaverPolicyId != null)
        {
            query = query.Where(r => r.RegsaverPolicyId == regsaverPolicyId);
        }

        return await query
            .OrderByDescending(r => r.BActive)
            .ToListAsync(cancellationToken);
    }

    public async Task SetNotificationSentAsync(Guid registrationId, bool sent, CancellationToken cancellationToken = default)
    {
        var registration = await _context.Registrations.FindAsync(new object[] { registrationId }, cancellationToken: cancellationToken);
        if (registration != null)
        {
            registration.BConfirmationSent = sent;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SynchronizeClubRepFinancialsAsync(
        Guid clubRepRegistrationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Aggregate active team financials, excluding WAITLIST/DROPPED agegroups
        var totals = await _context.Teams
            .Include(t => t.Agegroup)
            .Where(t => t.ClubrepRegistrationid == clubRepRegistrationId
                && t.Active == true
                && t.Agegroup != null
                && !t.Agegroup.AgegroupName.Contains("WAITLIST")
                && !t.Agegroup.AgegroupName.Contains("DROPPED"))
            .GroupBy(t => 1) // Dummy groupby to enable aggregate
            .Select(g => new
            {
                FeeBase = g.Sum(t => t.FeeBase ?? 0),
                FeeDiscount = g.Sum(t => t.FeeDiscount ?? 0),
                FeeDiscountMp = g.Sum(t => t.FeeDiscountMp ?? 0),
                FeeProcessing = g.Sum(t => t.FeeProcessing ?? 0),
                FeeDonation = g.Sum(t => t.FeeDonation ?? 0),
                FeeLatefee = g.Sum(t => t.FeeLatefee ?? 0),
                FeeTotal = g.Sum(t => t.FeeTotal ?? 0),
                OwedTotal = g.Sum(t => t.OwedTotal ?? 0),
                PaidTotal = g.Sum(t => t.PaidTotal ?? 0)
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Update club rep registration with aggregated totals
        var registration = await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == clubRepRegistrationId, cancellationToken);

        if (registration != null)
        {
            registration.FeeBase = totals?.FeeBase ?? 0;
            registration.FeeDiscount = totals?.FeeDiscount ?? 0;
            registration.FeeDiscountMp = totals?.FeeDiscountMp ?? 0;
            registration.FeeProcessing = totals?.FeeProcessing ?? 0;
            registration.FeeDonation = totals?.FeeDonation ?? 0;
            registration.FeeLatefee = totals?.FeeLatefee ?? 0;
            registration.FeeTotal = totals?.FeeTotal ?? 0;
            registration.OwedTotal = totals?.OwedTotal ?? 0;
            registration.PaidTotal = totals?.PaidTotal ?? 0;
            registration.Modified = DateTime.UtcNow;
            registration.LebUserId = userId;

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<Registrations>> GetActivePlayerRegistrationsByTeamIdsAsync(
        Guid jobId, IReadOnlyCollection<Guid> teamIds, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.JobId == jobId
                && r.BActive == true
                && r.AssignedTeamId.HasValue
                && teamIds.Contains(r.AssignedTeamId.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ZeroFeesForTeamAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        var registrations = await _context.Registrations
            .Where(r => r.JobId == jobId && r.AssignedTeamId == teamId)
            .ToListAsync(cancellationToken);

        foreach (var reg in registrations)
        {
            reg.FeeBase = 0;
            reg.FeeProcessing = 0;
            reg.FeeDiscount = 0;
            reg.FeeDiscountMp = 0;
            reg.FeeDonation = 0;
            reg.FeeLatefee = 0;
            reg.FeeTotal = 0;
            reg.OwedTotal = 0;
            reg.Modified = DateTime.UtcNow;
        }

        if (registrations.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);

        return registrations.Count;
    }

    public async Task<List<ClubRegistrationInfo>> GetClubRegistrationsForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.RoleId == RoleConstants.ClubRep
                && r.ClubName != null)
            .Select(r => new ClubRegistrationInfo
            {
                RegistrationId = r.RegistrationId,
                ClubName = r.ClubName!,
                UserId = r.UserId ?? ""
            })
            .ToListAsync(ct);
    }

    // ── Registration Search methods ──

    public async Task<List<CadtClubNode>> GetCadtTreeForJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Get all active teams with club rep assignments for this job
        var teamRows = await (
            from t in _context.Teams.AsNoTracking()
            where t.JobId == jobId && t.Active == true && t.ClubrepRegistrationid != null
            join ag in _context.Agegroups.AsNoTracking()
                on t.AgegroupId equals ag.AgegroupId
            join div in _context.Divisions.AsNoTracking()
                on t.DivId equals (Guid?)div.DivId into divJoin
            from div in divJoin.DefaultIfEmpty()
            join reg in _context.Registrations.AsNoTracking()
                on t.ClubrepRegistrationid equals (Guid?)reg.RegistrationId into regJoin
            from reg in regJoin.DefaultIfEmpty()
            select new
            {
                t.TeamId,
                t.TeamName,
                t.AgegroupId,
                AgegroupName = ag.AgegroupName,
                AgegroupColor = ag.Color,
                DivId = div != null ? (Guid?)div.DivId : null,
                DivName = div != null ? div.DivName : null,
                ClubName = reg != null ? reg.ClubName : null
            }
        ).ToListAsync(ct);

        if (teamRows.Count == 0)
            return [];

        // Batch-load active player counts per team (single query)
        var teamIds = teamRows.Select(t => t.TeamId).ToList();
        var playerCounts = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.AssignedTeamId != null && teamIds.Contains(r.AssignedTeamId.Value) && r.BActive == true)
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TeamId, g => g.Count, ct);

        // Build CADT tree: Club → Agegroup → Division → Team (with counts)
        return teamRows
            .Select(t => new
            {
                t.TeamId,
                t.TeamName,
                t.AgegroupId,
                t.AgegroupName,
                t.AgegroupColor,
                t.DivId,
                t.DivName,
                ClubName = t.ClubName ?? "Unaffiliated",
                PlayerCount = playerCounts.GetValueOrDefault(t.TeamId, 0)
            })
            .GroupBy(t => t.ClubName)
            .OrderBy(c => c.Key)
            .Select(clubGroup => new CadtClubNode
            {
                ClubName = clubGroup.Key,
                TeamCount = clubGroup.Count(),
                PlayerCount = clubGroup.Sum(t => t.PlayerCount),
                Agegroups = clubGroup
                    .GroupBy(t => new { t.AgegroupId, t.AgegroupName, t.AgegroupColor })
                    .OrderBy(a => a.Key.AgegroupName)
                    .Select(agGroup => new CadtAgegroupNode
                    {
                        AgegroupId = agGroup.Key.AgegroupId,
                        AgegroupName = agGroup.Key.AgegroupName ?? "",
                        Color = agGroup.Key.AgegroupColor,
                        TeamCount = agGroup.Count(),
                        PlayerCount = agGroup.Sum(t => t.PlayerCount),
                        Divisions = agGroup
                            .Where(t => t.DivId.HasValue)
                            .GroupBy(t => new { DivId = t.DivId!.Value, t.DivName })
                            .OrderBy(d => d.Key.DivName)
                            .Select(divGroup => new CadtDivisionNode
                            {
                                DivId = divGroup.Key.DivId,
                                DivName = divGroup.Key.DivName ?? "",
                                TeamCount = divGroup.Count(),
                                PlayerCount = divGroup.Sum(t => t.PlayerCount),
                                Teams = divGroup
                                    .OrderBy(t => t.TeamName)
                                    .Select(t => new CadtTeamNode
                                    {
                                        TeamId = t.TeamId,
                                        TeamName = t.TeamName ?? "",
                                        PlayerCount = t.PlayerCount
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();
    }

    public async Task<RegistrationSearchResponse> SearchAsync(
        Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default)
    {
        var query = _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId);

        // ── Multi-select status filters ──

        // Active status filter (multi-select: "True" / "False")
        if (request.ActiveStatuses is { Count: > 0 })
        {
            var boolValues = request.ActiveStatuses
                .Select(s => string.Equals(s, "True", StringComparison.OrdinalIgnoreCase))
                .ToList();
            query = query.Where(r => r.BActive != null && boolValues.Contains(r.BActive.Value));
        }

        // Pay status filter (multi-select: "PAID IN FULL" / "UNDER PAID" / "OVER PAID")
        if (request.PayStatuses is { Count: > 0 })
        {
            var hasPaid = request.PayStatuses.Contains("PAID IN FULL");
            var hasUnder = request.PayStatuses.Contains("UNDER PAID");
            var hasOver = request.PayStatuses.Contains("OVER PAID");
            query = query.Where(r =>
                (hasPaid && r.OwedTotal == 0) ||
                (hasUnder && r.OwedTotal > 0) ||
                (hasOver && r.OwedTotal < 0));
        }

        // Payment Method / Discount Code combined filter
        // "dc:*" = any discount code; "dc:CODE" = specific code; others = payment method names
        if (request.PaymentTypes is { Count: > 0 })
        {
            var payMethods = request.PaymentTypes.Where(v => !v.StartsWith("dc:")).ToList();
            var dcValues = request.PaymentTypes.Where(v => v.StartsWith("dc:")).Select(v => v[3..]).ToList();
            var allDc = dcValues.Contains("*");
            var specificDcCodes = dcValues.Where(v => v != "*").ToList();

            // Build OR predicate: matches payment method OR matches discount code
            if (payMethods.Count > 0 && (allDc || specificDcCodes.Count > 0))
            {
                query = query.Where(r =>
                    r.RegistrationAccounting.Any(a => a.Active == true && payMethods.Contains(a.PaymentMethod.PaymentMethod!))
                    || (allDc && r.DiscountCode != null)
                    || (specificDcCodes.Count > 0 && r.DiscountCode != null && specificDcCodes.Contains(r.DiscountCode.CodeName)));
            }
            else if (payMethods.Count > 0)
            {
                query = query.Where(r =>
                    r.RegistrationAccounting.Any(a => a.Active == true && payMethods.Contains(a.PaymentMethod.PaymentMethod!)));
            }
            else if (allDc)
            {
                query = query.Where(r => r.DiscountCode != null);
            }
            else if (specificDcCodes.Count > 0)
            {
                query = query.Where(r =>
                    r.DiscountCode != null && specificDcCodes.Contains(r.DiscountCode.CodeName));
            }
        }

        // ── Text filters ──

        // Name filter (searches first + last, supports "John Smith" split)
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var parts = request.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var first = parts[0];
                var last = parts[1];
                query = query.Where(r => r.User != null
                    && r.User.FirstName != null && r.User.FirstName.Contains(first)
                    && r.User.LastName != null && r.User.LastName.Contains(last));
            }
            else
            {
                var term = parts[0];
                query = query.Where(r => r.User != null
                    && ((r.User.FirstName != null && r.User.FirstName.Contains(term))
                        || (r.User.LastName != null && r.User.LastName.Contains(term))));
            }
        }

        // Email filter
        if (!string.IsNullOrWhiteSpace(request.Email))
            query = query.Where(r => r.User != null && r.User.Email != null && r.User.Email.Contains(request.Email));

        // Phone filter
        if (!string.IsNullOrWhiteSpace(request.Phone))
            query = query.Where(r => r.User != null && r.User.Cellphone != null && r.User.Cellphone.Contains(request.Phone));

        // School name filter
        if (!string.IsNullOrWhiteSpace(request.SchoolName))
            query = query.Where(r => r.SchoolName != null && r.SchoolName.Contains(request.SchoolName));

        // Invoice number filter (searches accounting records)
        if (!string.IsNullOrWhiteSpace(request.InvoiceNumber))
            query = query.Where(r => r.RegistrationAccounting
                .Any(a => a.AdnInvoiceNo != null && a.AdnInvoiceNo.Contains(request.InvoiceNumber)));

        // ── Multi-select entity filters ──

        // Roles (multi-select) — with synthetic sentinel handling
        if (request.RoleIds is { Count: > 0 })
        {
            var hasPlayerNotWaitlisted = request.RoleIds.Contains(RoleConstants.PlayerNotWaitlisted);
            var hasClubRepActiveTeams = request.RoleIds.Contains(RoleConstants.ClubRepActiveTeams);

            // Standard role IDs (strip out sentinels)
            var standardRoleIds = request.RoleIds
                .Where(id => id != RoleConstants.PlayerNotWaitlisted && id != RoleConstants.ClubRepActiveTeams)
                .ToList();

            // Build registration ID lists for synthetic filters (sequential — DbContext safety)
            List<Guid>? playerNotWaitlistedRegIds = null;
            if (hasPlayerNotWaitlisted)
            {
                playerNotWaitlistedRegIds = await _context.Registrations
                    .AsNoTracking()
                    .Where(r =>
                        r.JobId == jobId
                        && r.RoleId == RoleConstants.Player
                        && r.BActive == true
                        && r.AssignedTeam != null
                        && r.AssignedTeam.Active == true
                        && !r.AssignedTeam.Agegroup!.AgegroupName!.Contains("WAITLIST - ")
                        && !r.AssignedTeam.Agegroup!.AgegroupName!.Contains("Dropped Teams"))
                    .Select(r => r.RegistrationId)
                    .ToListAsync(ct);
            }

            List<Guid>? clubRepActiveRegIds = null;
            if (hasClubRepActiveTeams)
            {
                clubRepActiveRegIds = await _context.Registrations
                    .AsNoTracking()
                    .Where(r =>
                        r.JobId == jobId
                        && r.RoleId == RoleConstants.ClubRep
                        && r.BActive == true
                        && _context.Teams.Any(t =>
                            t.JobId == jobId
                            && t.ClubrepRegistrationid == r.RegistrationId
                            && t.Active == true
                            && !t.Agegroup!.AgegroupName!.Contains("WAITLIST - ")
                            && !t.Agegroup!.AgegroupName!.Contains("Dropped Teams")))
                    .Select(r => r.RegistrationId)
                    .ToListAsync(ct);
            }

            // Apply combined OR filter
            query = query.Where(r =>
                (standardRoleIds.Count > 0 && r.RoleId != null && standardRoleIds.Contains(r.RoleId))
                || (playerNotWaitlistedRegIds != null && playerNotWaitlistedRegIds.Contains(r.RegistrationId))
                || (clubRepActiveRegIds != null && clubRepActiveRegIds.Contains(r.RegistrationId)));
        }

        // LADT tree filters: OR across levels (tree selections at different levels are unioned)
        var hasTeamIds = request.TeamIds is { Count: > 0 };
        var hasAgegroupIds = request.AgegroupIds is { Count: > 0 };
        var hasDivisionIds = request.DivisionIds is { Count: > 0 };

        if (hasTeamIds || hasAgegroupIds || hasDivisionIds)
        {
            query = query.Where(r =>
                (hasTeamIds && r.AssignedTeamId != null && request.TeamIds!.Contains(r.AssignedTeamId.Value)) ||
                (hasAgegroupIds && r.AssignedAgegroupId != null && request.AgegroupIds!.Contains(r.AssignedAgegroupId.Value)) ||
                (hasDivisionIds && r.AssignedDivId != null && request.DivisionIds!.Contains(r.AssignedDivId.Value))
            );
        }

        // Club names (multi-select)
        if (request.ClubNames is { Count: > 0 })
            query = query.Where(r => r.ClubName != null && request.ClubNames.Contains(r.ClubName));

        // ── Multi-select demographic filters ──

        // Genders (multi-select, from User)
        if (request.Genders is { Count: > 0 })
            query = query.Where(r => r.User != null && r.User.Gender != null && request.Genders.Contains(r.User.Gender));

        // Positions (multi-select)
        if (request.Positions is { Count: > 0 })
            query = query.Where(r => r.Position != null && request.Positions.Contains(r.Position));

        // Grad years (multi-select)
        if (request.GradYears is { Count: > 0 })
            query = query.Where(r => r.GradYear != null && request.GradYears.Contains(r.GradYear));

        // Grades (multi-select)
        if (request.Grades is { Count: > 0 })
            query = query.Where(r => r.SchoolGrade != null && request.Grades.Contains(r.SchoolGrade));

        // Age ranges (multi-select, cross-join with JobAgeRanges)
        if (request.AgeRangeIds is { Count: > 0 })
        {
            query = query.Where(r => r.User != null && r.User.Dob != null
                && _context.JobAgeRanges.Any(jar =>
                    jar.JobId == jobId
                    && request.AgeRangeIds.Contains(jar.AgeRangeId)
                    && r.User.Dob >= jar.RangeLeft
                    && r.User.Dob <= jar.RangeRight));
        }

        // ── Multi-select billing & mobile filters ──

        // ARB subscription statuses (multi-select)
        if (request.ArbSubscriptionStatuses is { Count: > 0 })
        {
            var allowedSubStatuses = new List<string> { "active", "suspended" };
            var hasPaying = request.ArbSubscriptionStatuses.Contains("PAYING BY SUBSCRIPTION");
            var hasNotPaying = request.ArbSubscriptionStatuses.Contains("NOT PAYING BY SUBSCRIPTION");
            if (hasPaying && !hasNotPaying)
                query = query.Where(r => r.FeeTotal > 0 && r.AdnSubscriptionStatus != null
                    && allowedSubStatuses.Contains(r.AdnSubscriptionStatus));
            else if (hasNotPaying && !hasPaying)
                query = query.Where(r => r.AdnSubscriptionStatus == null
                    || !allowedSubStatuses.Contains(r.AdnSubscriptionStatus));
            // If both selected, no filter needed (all records match)
        }

        // Mobile registrations (multi-select by role name)
        if (request.MobileRegistrationRoles is { Count: > 0 })
            query = query.Where(r => r.ModifiedMobile != null && r.Role != null
                && r.Role.Name != null && request.MobileRegistrationRoles.Contains(r.Role.Name));

        // ── Date range ──
        if (request.RegDateFrom.HasValue)
            query = query.Where(r => r.RegistrationTs >= request.RegDateFrom.Value);
        if (request.RegDateTo.HasValue)
            query = query.Where(r => r.RegistrationTs <= request.RegDateTo.Value);

        // ── Club roster threshold search (with optional club name filter) ──
        if (request.RosterThreshold != null)
        {
            var teamsQuery = _context.Teams
                .Where(t => t.JobId == jobId && t.ClubrepRegistrationid != null && t.Active == true);

            // Narrow to specific club rep clubs when specified
            if (request.RosterThresholdClubNames is { Count: > 0 })
            {
                teamsQuery = teamsQuery.Where(t =>
                    t.ClubrepRegistration != null
                    && t.ClubrepRegistration.ClubName != null
                    && request.RosterThresholdClubNames.Contains(t.ClubrepRegistration.ClubName));
            }

            var underRosteredClubRepIds = teamsQuery
                .Where(t => _context.Registrations
                    .Count(r => r.AssignedTeamId == t.TeamId && r.BActive == true) <= request.RosterThreshold.Value)
                .Select(t => t.ClubrepRegistrationid!.Value)
                .Distinct();

            query = query.Where(r => underRosteredClubRepIds.Contains(r.RegistrationId));
        }

        // ── CADT tree filter (team ownership via ClubRepRegistrationId) ──
        if (request.CadtTeamIds is { Count: > 0 })
            query = query.Where(r => r.AssignedTeamId != null
                && request.CadtTeamIds.Contains(r.AssignedTeamId.Value));

        // Compute count + aggregates BEFORE paging
        var aggregates = await query
            .GroupBy(r => 1)
            .Select(g => new
            {
                Count = g.Count(),
                TotalFees = g.Sum(r => r.FeeTotal),
                TotalPaid = g.Sum(r => r.PaidTotal),
                TotalOwed = g.Sum(r => r.OwedTotal)
            })
            .FirstOrDefaultAsync(ct);

        // Project to DTO with joins
        var projected = query.Select(r => new RegistrationSearchResultDto
        {
            RegistrationId = r.RegistrationId,
            RegistrationAi = r.RegistrationAi,
            FirstName = r.User != null ? r.User.FirstName ?? "" : "",
            LastName = r.User != null ? r.User.LastName ?? "" : "",
            Email = r.User != null ? r.User.Email ?? "" : "",
            Phone = r.User != null ? r.User.Cellphone : null,
            Dob = r.User != null ? r.User.Dob : null,
            RoleName = r.Role != null ? r.Role.Name ?? "" : "",
            Active = r.BActive ?? false,
            Position = r.Position,
            TeamName = r.AssignedTeam != null ? r.AssignedTeam.TeamName : null,
            AgegroupName = r.AssignedTeam != null && r.AssignedTeam.Agegroup != null
                ? r.AssignedTeam.Agegroup.AgegroupName : null,
            DivisionName = r.AssignedTeam != null && r.AssignedTeam.Div != null
                ? r.AssignedTeam.Div.DivName : null,
            ClubName = r.ClubName,
            ClubRepClubName = r.AssignedTeam != null && r.AssignedTeam.ClubrepRegistration != null
                ? r.AssignedTeam.ClubrepRegistration.ClubName : null,
            FeeTotal = r.FeeTotal,
            PaidTotal = r.PaidTotal,
            OwedTotal = r.OwedTotal,
            RegistrationTs = r.RegistrationTs,
            Modified = r.Modified,
            DiscountCodeName = r.DiscountCode != null ? r.DiscountCode.CodeName : null,
            EmailOptOut = r.BemailOptOut
        });

        // Default sort (sorting done client-side)
        var results = await projected
            .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
            .ToListAsync(ct);

        // Post-materialization: format phones + compute assignment display string
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var parts = new[] { r.ClubRepClubName, r.AgegroupName, r.TeamName }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            // If no team assignment, fall back to registration's own ClubName (club reps)
            var assignment = parts.Any()
                ? string.Join(" ", parts)
                : !string.IsNullOrWhiteSpace(r.ClubName) ? r.ClubName : null;
            results[i] = r with
            {
                Phone = r.Phone.FormatPhone(),
                Assignment = assignment
            };
        }

        return new RegistrationSearchResponse
        {
            Result = results,
            Count = aggregates?.Count ?? 0,
            TotalFees = aggregates?.TotalFees ?? 0,
            TotalPaid = aggregates?.TotalPaid ?? 0,
            TotalOwed = aggregates?.TotalOwed ?? 0
        };
    }

    public async Task<RegistrationFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Base query: all registrations for this job with a user
        var baseQuery = _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.UserId != null);

        // ── Dynamic GroupBy categories (sequential — DbContext is not thread-safe) ──

        var roles = await baseQuery
            .Where(r => r.RoleId != null && r.Role != null)
            .GroupBy(r => new { r.RoleId, r.Role!.Name })
            .OrderBy(g => g.Key.Name)
            .Select(g => new FilterOption { Value = g.Key.RoleId!, Text = g.Key.Name ?? "", Count = g.Count() })
            .ToListAsync(ct);

        // ── Synthetic "not waitlisted" role filters (mirrors legacy Search/Index) ──

        var playerNotWaitlistedCount = await baseQuery
            .CountAsync(r =>
                r.RoleId == RoleConstants.Player
                && r.BActive == true
                && r.AssignedTeam != null
                && r.AssignedTeam.Active == true
                && !r.AssignedTeam.Agegroup!.AgegroupName!.Contains("WAITLIST - ")
                && !r.AssignedTeam.Agegroup!.AgegroupName!.Contains("Dropped Teams"), ct);

        if (playerNotWaitlistedCount > 0)
        {
            roles.Add(new FilterOption
            {
                Value = RoleConstants.PlayerNotWaitlisted,
                Text = "Player (NOT WAITLISTED)",
                Count = playerNotWaitlistedCount
            });
        }

        var clubRepActiveTeamsCount = await baseQuery
            .Where(r =>
                r.RoleId == RoleConstants.ClubRep
                && r.BActive == true)
            .Where(r => _context.Teams.Any(t =>
                t.JobId == jobId
                && t.ClubrepRegistrationid == r.RegistrationId
                && t.Active == true
                && !t.Agegroup!.AgegroupName!.Contains("WAITLIST - ")
                && !t.Agegroup!.AgegroupName!.Contains("Dropped Teams")))
            .CountAsync(ct);

        if (clubRepActiveTeamsCount > 0)
        {
            roles.Add(new FilterOption
            {
                Value = RoleConstants.ClubRepActiveTeams,
                Text = "Club Rep (ACTIVE, NOT WAITLISTED)",
                Count = clubRepActiveTeamsCount
            });
        }

        var teams = await baseQuery
            .Where(r => r.AssignedTeamId != null && r.AssignedTeam != null)
            .GroupBy(r => new { r.AssignedTeamId, r.AssignedTeam!.TeamName })
            .OrderBy(g => g.Key.TeamName)
            .Select(g => new FilterOption { Value = g.Key.AssignedTeamId!.Value.ToString(), Text = g.Key.TeamName ?? "", Count = g.Count() })
            .ToListAsync(ct);

        var agegroups = await baseQuery
            .Where(r => r.AssignedAgegroupId != null)
            .Join(_context.Agegroups, r => r.AssignedAgegroupId, ag => ag.AgegroupId, (r, ag) => ag)
            .GroupBy(ag => new { ag.AgegroupId, ag.AgegroupName })
            .OrderBy(g => g.Key.AgegroupName)
            .Select(g => new FilterOption { Value = g.Key.AgegroupId.ToString(), Text = g.Key.AgegroupName ?? "", Count = g.Count() })
            .ToListAsync(ct);

        var divisions = await baseQuery
            .Where(r => r.AssignedDivId != null)
            .Join(_context.Divisions, r => r.AssignedDivId, d => d.DivId, (r, d) => d)
            .GroupBy(d => new { d.DivId, d.DivName })
            .OrderBy(g => g.Key.DivName)
            .Select(g => new FilterOption { Value = g.Key.DivId.ToString(), Text = g.Key.DivName ?? "", Count = g.Count() })
            .ToListAsync(ct);

        var clubs = await baseQuery
            .Where(r => r.ClubName != null && r.ClubName != "")
            .GroupBy(r => r.ClubName!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var positions = await baseQuery
            .Where(r => r.BActive == true && r.Position != null)
            .GroupBy(r => r.Position!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var genders = await baseQuery
            .Where(r => r.BActive == true && r.User != null && r.User.Gender != null)
            .GroupBy(r => r.User!.Gender!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count(), DefaultChecked = true })
            .ToListAsync(ct);

        var gradYears = await baseQuery
            .Where(r => r.BActive == true && r.GradYear != null && r.GradYear != "")
            .GroupBy(r => r.GradYear!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var grades = await baseQuery
            .Where(r => r.BActive == true && r.SchoolGrade != null && r.SchoolGrade != "")
            .GroupBy(r => r.SchoolGrade!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var mobileRegs = await baseQuery
            .Where(r => r.BActive == true && r.ModifiedMobile != null && r.Role != null)
            .GroupBy(r => r.Role!.Name!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // ── Active status (group by BActive) ──
        var activeStatuses = await baseQuery
            .GroupBy(r => r.BActive)
            .OrderByDescending(g => g.Key)
            .Select(g => new FilterOption
            {
                Value = g.Key.ToString()!,
                Text = (g.Key ?? false) ? "Active" : "Inactive",
                Count = g.Count(),
                DefaultChecked = g.Key == true
            })
            .ToListAsync(ct);

        // ── Pay status (fixed-value counts) ──
        var activeBase = baseQuery.Where(r => r.BActive == true);
        var paidCount = await activeBase.CountAsync(r => r.OwedTotal == 0, ct);
        var underpaidCount = await activeBase.CountAsync(r => r.OwedTotal > 0, ct);
        var overpaidCount = await activeBase.CountAsync(r => r.OwedTotal < 0, ct);

        // ── ARB subscription status (fixed-value counts) ──
        var allowedSubStatuses = new List<string> { "active", "suspended" };
        var withSubCount = await activeBase
            .CountAsync(r => r.FeeTotal > 0 && r.AdnSubscriptionStatus != null
                && allowedSubStatuses.Contains(r.AdnSubscriptionStatus), ct);
        var withoutSubCount = await activeBase
            .CountAsync(r => r.AdnSubscriptionStatus == null
                || !allowedSubStatuses.Contains(r.AdnSubscriptionStatus), ct);

        // ── Age ranges (cross-join with JobAgeRanges, match DOB in range) ──
        var ageRanges = await (
            from r in baseQuery
            from jar in _context.JobAgeRanges
            where r.BActive == true
                && jar.JobId == jobId
                && r.User != null && r.User.Dob != null
                && r.User.Dob >= jar.RangeLeft && r.User.Dob <= jar.RangeRight
            group jar by new { jar.AgeRangeId, jar.RangeName } into g
            orderby g.Key.RangeName
            select new FilterOption
            {
                Value = g.Key.AgeRangeId.ToString(),
                Text = g.Key.RangeName ?? "",
                Count = g.Count()
            }
        ).ToListAsync(ct);

        // Build pay status list from computed counts
        var payStatuses = new List<FilterOption>
        {
            new() { Value = "PAID IN FULL", Text = "PAID IN FULL", Count = paidCount },
            new() { Value = "UNDER PAID", Text = "UNDER PAID", Count = underpaidCount },
            new() { Value = "OVER PAID", Text = "OVER PAID", Count = overpaidCount }
        };

        // Build ARB subscription list from computed counts
        var arbStatuses = new List<FilterOption>
        {
            new() { Value = "PAYING BY SUBSCRIPTION", Text = "PAYING BY SUBSCRIPTION", Count = withSubCount },
            new() { Value = "NOT PAYING BY SUBSCRIPTION", Text = "NOT PAYING BY SUBSCRIPTION", Count = withoutSubCount }
        };

        // ── Club rep clubs (distinct club names from club rep registrations with active teams) ──
        var clubRepClubs = await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId
                && t.Active == true
                && t.ClubrepRegistrationid != null
                && t.ClubrepRegistration != null
                && t.ClubrepRegistration.BActive == true
                && t.ClubrepRegistration.ClubName != null
                && t.ClubrepRegistration.ClubName != "")
            .GroupBy(t => t.ClubrepRegistration!.ClubName!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Payment methods — from accounting records
        var paymentMethods = await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(a => a.Registration != null && a.Registration.JobId == jobId && a.Active == true)
            .GroupBy(a => a.PaymentMethod.PaymentMethod!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Discount codes — from registration entity with detail (%, flat, amount)
        var dcRaw = await baseQuery
            .Where(r => r.DiscountCode != null)
            .GroupBy(r => new { r.DiscountCode!.CodeName, r.DiscountCode.BAsPercent, r.DiscountCode.CodeAmount })
            .OrderBy(g => g.Key.CodeName)
            .Select(g => new { g.Key.CodeName, g.Key.BAsPercent, g.Key.CodeAmount, Count = g.Count() })
            .ToListAsync(ct);

        var discountCodeOptions = dcRaw.Select(dc =>
        {
            var detail = dc.BAsPercent
                ? $"{dc.CodeAmount:0}%"
                : $"${dc.CodeAmount:0.00}";
            return new FilterOption { Value = "dc:" + dc.CodeName, Text = $"DC: {dc.CodeName} ({detail})", Count = dc.Count };
        }).ToList();

        var totalDcCount = dcRaw.Sum(dc => dc.Count);
        var paymentTypes = paymentMethods.ToList();
        if (discountCodeOptions.Count > 0)
        {
            paymentTypes.Add(new FilterOption { Value = "dc:*", Text = $"ALL Discount Codes", Count = totalDcCount });
            paymentTypes.AddRange(discountCodeOptions);
        }

        return new RegistrationFilterOptionsDto
        {
            Roles = roles,
            Teams = teams,
            Agegroups = agegroups,
            Divisions = divisions,
            Clubs = clubs,
            ActiveStatuses = activeStatuses,
            PayStatuses = payStatuses,
            Genders = genders,
            Positions = positions,
            GradYears = gradYears,
            Grades = grades,
            AgeRanges = ageRanges,
            ArbSubscriptionStatuses = arbStatuses,
            MobileRegistrations = mobileRegs,
            ClubRepClubs = clubRepClubs,
            PaymentTypes = paymentTypes
        };
    }

    public async Task<RegistrationDetailDto?> GetRegistrationDetailAsync(
        Guid registrationId, Guid jobId, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Role)
            .Include(r => r.AssignedTeam)
                .ThenInclude(t => t!.Agegroup)
            .Include(r => r.AssignedTeam)
                .ThenInclude(t => t!.ClubrepRegistration)
            .Include(r => r.Job)
                .ThenInclude(j => j.Sport)
            .Include(r => r.FamilyUser)
            .Include(r => r.RegistrationAccounting)
                .ThenInclude(a => a.PaymentMethod)
            .Where(r => r.RegistrationId == registrationId && r.JobId == jobId)
            .FirstOrDefaultAsync(ct);

        if (reg == null) return null;

        // Resolve account username and family account demographics
        string? accountUsername = reg.User?.UserName;
        UserDemographicsDto? familyAccountDemographics = null;
        if (!string.IsNullOrEmpty(reg.FamilyUserId))
        {
            var familyUser = await _context.AspNetUsers
                .AsNoTracking()
                .Where(u => u.Id == reg.FamilyUserId)
                .FirstOrDefaultAsync(ct);

            if (familyUser != null)
            {
                accountUsername = familyUser.UserName ?? accountUsername;
                familyAccountDemographics = new UserDemographicsDto
                {
                    Email = familyUser.Email,
                    Cellphone = familyUser.Cellphone,
                    StreetAddress = familyUser.StreetAddress,
                    City = familyUser.City,
                    State = familyUser.State,
                    PostalCode = familyUser.PostalCode,
                };
            }
        }

        // Build profile values from entity columns using reflection
        var profileValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var profileProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Act", "BackcheckExplain", "BgCheckDate", "CertDate", "CertNo", "ClassRank",
            "ClubName", "Gpa", "GradYear", "HealthInsurer", "HealthInsurerGroupNo",
            "HealthInsurerPhone", "HealthInsurerPolicyNo", "HeightInches", "InsuredName",
            "JerseySize", "Kilt", "MedicalNote", "Position", "SchoolTeamName", "Psat",
            "Region", "RoommatePref", "Sat", "SatMath", "SatVerbal", "SatWriting",
            "SchoolGrade", "SchoolName", "Shoes", "ShortsSize", "SportAssnId",
            "SportAssnIdexpDate", "SportYearsExp", "TShirt", "UniformNo",
            "VolChildreninprogram", "Volposition", "WeightLbs", "NightGroup", "DayGroup",
            "SpecialRequests", "Reversible", "Gloves", "Sweatshirt", "FiveTenFive",
            "Threehundredshuttle", "Fourtyyarddash", "Fastestshot", "BCollegeCommit",
            "CollegeCommit", "WhoReferred", "StrongHand", "ClubCoach", "ClubCoachEmail",
            "SchoolCoach", "SchoolCoachEmail", "SchoolActivities", "HonorsAcademic",
            "HonorsAthletic", "OtherSports", "HeadshotPath", "SchoolLevelClasses",
            "Height", "MomTwitter", "DadTwitter", "MomInstagram", "DadInstagram",
            "Twitter", "Instagram", "ClubTeamName", "Sweatpants", "SkillLevel",
            "Snapchat", "TikTokHandle", "RecruitingHandle", "PreviousCoach1", "PreviousCoach2"
        };

        var regType = typeof(Registrations);
        foreach (var propName in profileProps)
        {
            var prop = regType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                var val = prop.GetValue(reg);
                profileValues[propName] = val?.ToString();
            }
        }

        // Build accounting records
        var accountingRecords = reg.RegistrationAccounting
            .OrderByDescending(a => a.Createdate)
            .Select(a => new AccountingRecordDto
            {
                AId = a.AId,
                Date = a.Createdate,
                PaymentMethod = a.PaymentMethod?.PaymentMethod ?? a.Paymeth ?? "",
                DueAmount = a.Dueamt,
                PaidAmount = a.Payamt,
                Comment = a.Comment,
                CheckNo = a.CheckNo,
                PromoCode = a.PromoCode,
                Active = a.Active,
                AdnTransactionId = a.AdnTransactionId,
                AdnCc4 = a.AdnCc4,
                AdnCcExpDate = a.AdnCcexpDate,
                AdnInvoiceNo = a.AdnInvoiceNo,
                CanRefund = !string.IsNullOrWhiteSpace(a.AdnTransactionId)
                    && (a.PaymentMethod?.PaymentMethod?.Contains("Credit Card") == true
                        || (a.Paymeth != null && a.Paymeth.Contains("Credit Card")))
            })
            .ToList();

        // Detect club rep: any active team references this registration as its club rep
        var isClubRep = await _context.Teams
            .AnyAsync(t => t.ClubrepRegistrationid == reg.RegistrationId && t.Active == true, ct);

        return new RegistrationDetailDto
        {
            RegistrationId = reg.RegistrationId,
            RegistrationAi = reg.RegistrationAi,
            FirstName = reg.User?.FirstName ?? "",
            LastName = reg.User?.LastName ?? "",
            Email = reg.User?.Email ?? "",
            Phone = reg.User?.Cellphone.FormatPhone(),
            RoleName = reg.Role?.Name ?? "",
            Active = reg.BActive ?? false,
            TeamName = reg.AssignedTeam?.TeamName,
            Assignment = reg.AssignedTeam != null
                ? string.Join(" ", new[] {
                    reg.AssignedTeam.ClubrepRegistration?.ClubName,
                    reg.AssignedTeam.Agegroup?.AgegroupName,
                    reg.AssignedTeam.TeamName
                  }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } a ? a : null
                : reg.ClubName,
            FeeBase = reg.FeeBase,
            FeeProcessing = reg.FeeProcessing,
            FeeDiscount = reg.FeeDiscount,
            FeeTotal = reg.FeeTotal,
            PaidTotal = reg.PaidTotal,
            OwedTotal = reg.OwedTotal,
            ProfileValues = profileValues,
            AccountUsername = accountUsername,
            ProfileMetadataJson = reg.Job?.PlayerProfileMetadataJson,
            SportName = reg.Job?.Sport?.SportName,
            JsonOptions = reg.Job?.JsonOptions,
            MomLabel = !string.IsNullOrWhiteSpace(reg.Job?.MomLabel) ? reg.Job.MomLabel : "Mom",
            DadLabel = !string.IsNullOrWhiteSpace(reg.Job?.DadLabel) ? reg.Job.DadLabel : "Dad",
            FamilyContact = reg.FamilyUser != null ? new FamilyContactDto
            {
                MomFirstName = reg.FamilyUser.MomFirstName,
                MomLastName = reg.FamilyUser.MomLastName,
                MomCellphone = reg.FamilyUser.MomCellphone,
                MomEmail = reg.FamilyUser.MomEmail,
                DadFirstName = reg.FamilyUser.DadFirstName,
                DadLastName = reg.FamilyUser.DadLastName,
                DadCellphone = reg.FamilyUser.DadCellphone,
                DadEmail = reg.FamilyUser.DadEmail,
            } : null,
            FamilyAccountDemographics = familyAccountDemographics,
            UserDemographics = reg.User != null ? new UserDemographicsDto
            {
                Email = reg.User.Email,
                Cellphone = reg.User.Cellphone,
                Gender = reg.User.Gender,
                DateOfBirth = reg.User.Dob,
                StreetAddress = reg.User.StreetAddress,
                City = reg.User.City,
                State = reg.User.State,
                PostalCode = reg.User.PostalCode,
            } : null,
            HasSubscription = !string.IsNullOrWhiteSpace(reg.AdnSubscriptionId),
            RegistrationDate = reg.RegistrationTs,
            ModifiedDate = reg.Modified,
            AccountingRecords = accountingRecords,
            IsClubRep = isClubRep
        };
    }

    public async Task UpdateRegistrationProfileAsync(
        Guid jobId, string userId, UpdateRegistrationProfileRequest request, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .Where(r => r.RegistrationId == request.RegistrationId && r.JobId == jobId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Registration not found or does not belong to this job.");

        var regType = typeof(Registrations);
        foreach (var (key, value) in request.ProfileValues)
        {
            var prop = regType.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite) continue;

            // Type conversion
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            if (value == null)
            {
                if (Nullable.GetUnderlyingType(prop.PropertyType) != null || !prop.PropertyType.IsValueType)
                    prop.SetValue(reg, null);
            }
            else if (targetType == typeof(string))
            {
                prop.SetValue(reg, value);
            }
            else if (targetType == typeof(DateTime) && DateTime.TryParse(value, out var dt))
            {
                prop.SetValue(reg, dt);
            }
            else if (targetType == typeof(bool) && bool.TryParse(value, out var b))
            {
                prop.SetValue(reg, b);
            }
            else if (targetType == typeof(double) && double.TryParse(value, out var d))
            {
                prop.SetValue(reg, d);
            }
            else if (targetType == typeof(decimal) && decimal.TryParse(value, out var dec))
            {
                prop.SetValue(reg, dec);
            }
            else if (targetType == typeof(int) && int.TryParse(value, out var i))
            {
                prop.SetValue(reg, i);
            }
            else if (targetType == typeof(string))
            {
                prop.SetValue(reg, value);
            }
        }

        reg.Modified = DateTime.UtcNow;
        reg.LebUserId = userId;

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateFamilyContactAsync(
        Guid jobId, string userId, UpdateFamilyContactRequest request, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == request.RegistrationId && r.JobId == jobId)
            .Select(r => new { r.FamilyUserId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Registration not found or does not belong to this job.");

        if (string.IsNullOrEmpty(reg.FamilyUserId))
            throw new InvalidOperationException("This registration has no linked family account.");

        var family = await _context.Families
            .Where(f => f.FamilyUserId == reg.FamilyUserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Family record not found.");

        var fc = request.FamilyContact;
        family.MomFirstName = fc.MomFirstName;
        family.MomLastName = fc.MomLastName;
        family.MomCellphone = fc.MomCellphone;
        family.MomEmail = fc.MomEmail;
        family.DadFirstName = fc.DadFirstName;
        family.DadLastName = fc.DadLastName;
        family.DadCellphone = fc.DadCellphone;
        family.DadEmail = fc.DadEmail;
        family.Modified = DateTime.UtcNow;
        family.LebUserId = userId;

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateUserDemographicsAsync(
        Guid jobId, string userId, UpdateUserDemographicsRequest request, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == request.RegistrationId && r.JobId == jobId)
            .Select(r => new { r.UserId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Registration not found or does not belong to this job.");

        var user = await _context.AspNetUsers
            .Where(u => u.Id == reg.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("User record not found.");

        var demo = request.Demographics;
        user.Email = demo.Email;
        user.Cellphone = demo.Cellphone;
        user.Gender = demo.Gender;
        user.Dob = demo.DateOfBirth;
        user.StreetAddress = demo.StreetAddress;
        user.City = demo.City;
        user.State = demo.State;
        user.PostalCode = demo.PostalCode;

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateFamilyAccountDemographicsAsync(
        Guid jobId, string userId, UpdateUserDemographicsRequest request, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == request.RegistrationId && r.JobId == jobId)
            .Select(r => new { r.FamilyUserId })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Registration not found or does not belong to this job.");

        if (string.IsNullOrEmpty(reg.FamilyUserId))
            throw new InvalidOperationException("This registration has no linked family account.");

        var user = await _context.AspNetUsers
            .Where(u => u.Id == reg.FamilyUserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Family account user not found.");

        var demo = request.Demographics;
        user.Email = demo.Email;
        user.Cellphone = demo.Cellphone;
        user.StreetAddress = demo.StreetAddress;
        user.City = demo.City;
        user.State = demo.State;
        user.PostalCode = demo.PostalCode;

        await _context.SaveChangesAsync(ct);
    }

    // ── Roster Swapper methods ──

    public async Task<List<SwapperPlayerDto>> GetRosterByTeamIdAsync(Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        var raw = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.AssignedTeamId == teamId && r.JobId == jobId)
            .Select(r => new
            {
                r.RegistrationId,
                LastName = r.User != null ? r.User.LastName ?? "" : "",
                FirstName = r.User != null ? r.User.FirstName ?? "" : "",
                RoleName = r.Role != null ? r.Role.Name ?? "" : "",
                BActive = r.BActive ?? true,
                School = r.SchoolName,
                GradeRaw = r.SchoolGrade,
                GradYearRaw = r.GradYear,
                r.Position,
                Dob = r.User != null ? r.User.Dob : (DateTime?)null,
                Gender = r.User != null ? r.User.Gender : null,
                r.SkillLevel,
                YrsExpRaw = r.SportYearsExp,
                Requests = r.SpecialRequests,
                PrevCoach = r.PreviousCoach1,
                r.FeeBase,
                r.FeeTotal,
                r.OwedTotal,
                r.RegistrationTs
            })
            .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
            .ToListAsync(ct);

        return raw.Select(r => new SwapperPlayerDto
        {
            RegistrationId = r.RegistrationId,
            PlayerName = string.IsNullOrWhiteSpace(r.LastName) && string.IsNullOrWhiteSpace(r.FirstName)
                ? "Unknown"
                : $"{r.LastName}, {r.FirstName}".Trim().TrimEnd(',').Trim(),
            RoleName = r.RoleName,
            BActive = r.BActive,
            School = r.School,
            Grade = short.TryParse(r.GradeRaw, out var g) ? g : null,
            GradYear = int.TryParse(r.GradYearRaw, out var gy) ? gy : null,
            Position = r.Position,
            Dob = r.Dob,
            Gender = r.Gender,
            SkillLevel = r.SkillLevel,
            YrsExp = int.TryParse(r.YrsExpRaw, out var ye) ? ye : null,
            Requests = r.Requests,
            PrevCoach = r.PrevCoach,
            FeeBase = r.FeeBase,
            FeeTotal = r.FeeTotal,
            OwedTotal = r.OwedTotal,
            RegistrationTs = r.RegistrationTs
        }).ToList();
    }

    public async Task<List<SwapperPlayerDto>> GetUnassignedAdultsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.Role!.Name == RoleConstants.Names.UnassignedAdultName)
            .Select(r => new SwapperPlayerDto
            {
                RegistrationId = r.RegistrationId,
                PlayerName = (r.User != null ? (r.User.LastName ?? "") + ", " + (r.User.FirstName ?? "") : "Unknown").Trim(),
                RoleName = r.Role != null ? r.Role.Name ?? "" : "",
                BActive = r.BActive ?? true,
                School = r.SchoolName,
                Grade = null,
                GradYear = null,
                Position = r.Position,
                Dob = r.User != null ? r.User.Dob : null,
                Gender = r.User != null ? r.User.Gender : null,
                SkillLevel = null,
                YrsExp = null,
                Requests = r.SpecialRequests,
                PrevCoach = null,
                FeeBase = r.FeeBase,
                FeeTotal = r.FeeTotal,
                OwedTotal = r.OwedTotal,
                RegistrationTs = r.RegistrationTs
            })
            .OrderBy(p => p.PlayerName)
            .ToListAsync(ct);
    }

    public async Task<List<Registrations>> GetRegistrationsForTransferAsync(
        List<Guid> registrationIds, Guid sourcePoolId, Guid jobId, CancellationToken ct = default)
    {
        var query = _context.Registrations
            .Include(r => r.Role)
            .Include(r => r.User)
            .Where(r => registrationIds.Contains(r.RegistrationId) && r.JobId == jobId);

        if (sourcePoolId == Guid.Empty)
        {
            // Source is Unassigned Adults pool — validate role
            query = query.Where(r => r.Role!.Name == RoleConstants.Names.UnassignedAdultName);
        }
        else
        {
            // Source is a team — validate AssignedTeamId
            query = query.Where(r => r.AssignedTeamId == sourcePoolId);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<Registrations?> GetExistingStaffAssignmentAsync(
        string userId, Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.UserId == userId
                && r.AssignedTeamId == teamId
                && r.JobId == jobId
                && r.RoleId == RoleConstants.Staff)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> FindMatchingRegistrationTeamAsync(
        Guid registrationId, Guid newJobId, CancellationToken ct = default)
    {
        // 1. Get current registration's assigned team characteristics
        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new { r.AssignedTeamId, r.GradYear })
            .FirstOrDefaultAsync(ct);

        if (reg?.AssignedTeamId == null)
            return null;

        var currentTeamKeys = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == reg.AssignedTeamId)
            .Select(t => new
            {
                AgegroupName = t.Agegroup.AgegroupName,
                DivName = t.Div!.DivName,
                t.Season,
                t.Year
            })
            .FirstOrDefaultAsync(ct);

        if (currentTeamKeys == null)
            return null;

        // 2. Get target job's primary league
        var newLeagueId = await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == newJobId)
            .Select(jl => jl.LeagueId)
            .FirstOrDefaultAsync(ct);

        if (newLeagueId == Guid.Empty)
            return null;

        // 3. Find matching team in the new job's league
        var gradYear = reg.GradYear ?? "";
        return await _context.Teams
            .AsNoTracking()
            .Where(t =>
                t.LeagueId == newLeagueId
                && t.Season == currentTeamKeys.Season
                && t.Year == currentTeamKeys.Year
                && t.Agegroup.AgegroupName == currentTeamKeys.AgegroupName
                && t.Div!.DivName == currentTeamKeys.DivName
                && t.TeamName!.Contains(gradYear))
            .Select(t => (Guid?)t.TeamId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetEmailOptOutAsync(Guid registrationId, bool optOut, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, ct);

        if (reg == null) return;

        reg.BemailOptOut = optOut;
        await _context.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid registrationId, bool active, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, ct);

        if (reg == null) return;

        reg.BActive = active;
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<UniformTemplateRow>> GetPlayerRosterForTemplateAsync(Guid jobId, CancellationToken ct = default)
    {
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join t in _context.Teams on r.AssignedTeamId equals t.TeamId
            where r.JobId == jobId
                  && r.RoleId == RoleConstants.Player
                  && r.BActive == true
            orderby t.TeamName, u.LastName, u.FirstName
            select new UniformTemplateRow
            {
                RegistrationId = r.RegistrationId,
                FirstName = u.FirstName ?? string.Empty,
                LastName = u.LastName ?? string.Empty,
                TeamName = t.TeamName ?? string.Empty,
                UniformNo = r.UniformNo,
                DayGroup = r.DayGroup
            }
        ).AsNoTracking().ToListAsync(ct);
    }
}
