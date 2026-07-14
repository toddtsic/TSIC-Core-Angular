using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.CampGroups;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Adults;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.SqlDbContext.Helpers;
using TSIC.Infrastructure.Utilities;

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

    public async Task<List<BatchRegistrantEmailDto>> GetRecipientEmailsByIdsAsync(
        IEnumerable<Guid> registrationIds,
        CancellationToken cancellationToken = default)
    {
        var ids = registrationIds.Distinct().ToList();
        if (ids.Count == 0) return new List<BatchRegistrantEmailDto>();

        return await _context.Registrations
            .AsNoTracking()
            .Where(r => ids.Contains(r.RegistrationId))
            .Select(r => new BatchRegistrantEmailDto
            {
                RegistrationId = r.RegistrationId,
                Email = r.User!.Email
            })
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

    public async Task<List<Guid>> GetCustomerIdsForFamilyUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.FamilyUserId == userId && r.RoleId == RoleConstants.Player)
            .Join(_context.Jobs, r => r.JobId, j => j.JobId, (r, j) => j.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetActiveFamilyJobIdsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.FamilyUserId == userId
                     && r.RoleId == RoleConstants.Player
                     && r.BActive == true)
            .Select(r => r.JobId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetCustomerIdsForClubRepUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.RoleId == RoleConstants.ClubRep)
            .Join(_context.Jobs, r => r.JobId, j => j.JobId, (r, j) => j.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetActiveClubRepJobIdsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.UserId == userId
                     && r.RoleId == RoleConstants.ClubRep
                     && r.BActive == true)
            .Select(r => r.JobId)
            .Distinct()
            .ToListAsync(cancellationToken);
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

    public async Task<List<Registrations>> GetFamilyRegistrationsForPlayersTrackedAsync(
        Guid jobId,
        string familyUserId,
        IReadOnlyCollection<string> playerIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
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

    public async Task<List<RegisteredPlayerInfo>> GetFamilyPlayersForAccountingAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.FamilyUserId == familyUserId
                && r.RoleId == RoleConstants.Player)
            .OrderBy(r => r.User!.FirstName)
            .Select(r => new RegisteredPlayerInfo
            {
                RegistrationId = r.RegistrationId,
                PlayerName = ((r.User!.FirstName ?? "") + " " + (r.User.LastName ?? "")).Trim(),
                Active = r.BActive ?? false,
                RegistrationTs = r.RegistrationTs,
                AssignedTeamId = r.AssignedTeamId,
                AgeGroupId = r.AssignedTeam != null ? r.AssignedTeam.AgegroupId : (Guid?)null,
                AssignedTeamName = r.AssignedTeam != null ? r.AssignedTeam.TeamName : null,
                AssignedAgeGroupName = r.AssignedTeam != null ? r.AssignedTeam.Agegroup!.AgegroupName : null,
                AssignedClubName = r.AssignedTeam != null && r.AssignedTeam.ClubrepRegistration != null
                    ? r.AssignedTeam.ClubrepRegistration.ClubName
                    : null,
                FeeBase = r.FeeBase,
                FeeProcessing = r.FeeProcessing,
                FeeDiscount = r.FeeDiscount,
                FeeDiscountMp = r.FeeDiscountMp,
                FeeLatefee = r.FeeLatefee,
                FeeTotal = r.FeeTotal,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal
            })
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

    public async Task<string?> GetChargeDescriptionAsync(
        Guid registrationId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Mirrors legacy GetPaymentSubmissionDescription_ByRegistrationId. Project the raw
        // columns (plain LEFT JOINs over the nullable navigations — no CASE/CONCAT to
        // translate) and assemble the colon-delimited string in memory, branching on
        // whether the player has an assigned team.
        var data = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId && r.JobId == jobId)
            .Select(r => new
            {
                r.AssignedTeamId,
                JobName = r.Job.JobName,
                First = r.User!.FirstName,
                Last = r.User.LastName,
                RoleName = r.Role!.Name,
                AgegroupName = r.AssignedTeam!.Agegroup!.AgegroupName,
                TeamName = r.AssignedTeam.TeamName,
                // Owning club, when the assigned team is rostered by a club rep.
                // Route: Teams.ClubrepRegistrationid -> Registrations.ClubName
                // (same source the family accounting ledger uses).
                ClubName = r.AssignedTeam.ClubrepRegistration != null
                    ? r.AssignedTeam.ClubrepRegistration.ClubName
                    : null
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (data == null)
            return null;

        if (data.AssignedTeamId == null)
            return $"{data.RoleName}:{data.First} {data.Last}";

        // Prefix the owning club name onto the team segment when present:
        // "{Club}: {Team}" mirrors the ledger label.
        var teamSegment = string.IsNullOrWhiteSpace(data.ClubName)
            ? data.TeamName
            : $"{data.ClubName}: {data.TeamName}";
        return $"{data.JobName}:{data.First} {data.Last}:{data.AgegroupName}:{teamSegment}";
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
        // Capacity count per team: confirmed members + in-flight reservations (PLAYERS).
        // A seat counts when BActive=1 (paid/check/free — forever) OR it is a provisional
        // reservation still inside the hold window (RegistrationTs > cutoff). An abandoned
        // cart past the window stops counting so its seat frees itself. Role-filtered so
        // staff dropped on a team via the swapper don't inflate player capacity. Canonical
        // source for the picker's rosterFull AND the PreSubmit capacity seed, so both agree
        // with the overflow decision (GetAssignedPlayerCountAsync). Cutoff is a captured
        // value → sent to SQL as a parameter, so every row measures against the same instant.
        var cutoff = SeatHoldPolicy.Cutoff();
        return await _context.Registrations
            .Where(r => r.AssignedTeamId != null && teamIds.Contains(r.AssignedTeamId.Value)
                     && r.RoleId == RoleConstants.Player
                     && (r.BActive == true || r.RegistrationTs > cutoff))
            .GroupBy(r => r.AssignedTeamId!.Value)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);
    }

    public async Task<bool> TryCommitSeatAsync(
        Registrations reg, CancellationToken cancellationToken = default)
    {
        // Atomically commit a player registration to a confirmed roster seat (BActive=true) —
        // but ONLY if the team has room: "hold a roster spot only if one is available." This is
        // THE guarded bActive→1 transition (pay-by-check, free $0, and the claim-then-charge step
        // of card/eCheck payment). Returns true when committed (or already active — idempotent),
        // false when the team is full so the caller routes the registrant to the waitlist.
        //
        // The gate counts CONFIRMED MEMBERS only (BActive=1), EXCLUDING this registration; an
        // unlimited team (MaxCount <= 0) always has room. Confirmed-only is the correct guarantee:
        // it makes "confirmed members never exceed MaxCount" an invariant, and — unlike counting
        // live holds here — it does not let two families' in-flight holds on the last seat block
        // each other (which would reject BOTH). Within-window holds still drive the picker/PreSubmit
        // capacity (display + early routing); they just don't gate the final commit. So the first
        // of two racers to reach commit wins the seat; the second gets false → waitlist.
        //
        // Serializable isolation makes the count-then-commit indivisible, so two simultaneous
        // commits on the last seat cannot both pass — the loser waits, re-counts, and gets false
        // (mirrors ScheduleRepository.ExecuteWeatherAdjustmentAsync). A lock-conversion deadlock
        // (SQL 1205) on a contended seat is retried once; Serializable is the primary guarantee,
        // the retry is the backstop. Pending field changes on the tracked reg are persisted in
        // the same transaction, so callers must leave only this reg dirty when calling.
        if (reg.AssignedTeamId is not { } teamId)
        {
            // No team to gate against — nothing to oversubscribe. Commit straight through.
            reg.BActive = true;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        var maxCount = await _context.Teams
            .Where(t => t.TeamId == teamId)
            .Select(t => t.MaxCount)
            .FirstOrDefaultAsync(cancellationToken);

        var retried = false;
        while (true)
        {
            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, cancellationToken);

                // Gate only a genuinely new commit on a capacity-limited team. An already-active
                // reg (idempotent re-submit) and an unlimited team (MaxCount <= 0, e.g. the
                // WAITLIST twin) skip the count and commit straight through.
                if (reg.BActive != true && maxCount > 0)
                {
                    var confirmedMembers = await _context.Registrations.CountAsync(
                        r => r.AssignedTeamId == teamId
                          && r.RegistrationId != reg.RegistrationId
                          && r.RoleId == RoleConstants.Player
                          && r.BActive == true,
                        cancellationToken);

                    if (confirmedMembers >= maxCount)
                    {
                        await tx.RollbackAsync(cancellationToken);
                        return false;
                    }
                }

                reg.BActive = true;
                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return true;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205 && !retried)
            {
                // Deadlock victim on a contended seat — retry once; the re-count resolves it.
                retried = true;
            }
        }
    }

    public async Task<bool> IsSeatAvailableAsync(
        Registrations reg, int reservedInBatch = 0, CancellationToken cancellationToken = default)
    {
        // Read-only sibling of TryCommitSeatAsync answering "will the finalize guard seat this
        // reg?" Used to PARTITION a payment cart BEFORE charging: a player whose seat is gone is
        // dropped from the charge set (never charged) and surfaced as needs-waitlist, while the
        // seatable players are charged. Mirrors the guard's decision EXACTLY — confirmed members
        // only (BActive=1), excluding self; an unlimited team (MaxCount <= 0, incl. the waitlist
        // twin) always has room; an already-active reg owns its seat — so the pre-charge prediction
        // agrees with the post-charge commit. No write, no transaction: a point-in-time read. The
        // authoritative overfill guard remains TryCommitSeatAsync at finalize; in the rare instant
        // where two carts both pass this read for the last seat, that guard still lets only one in.
        //
        // reservedInBatch: seats already claimed by EARLIER registrations in the SAME reconcile pass.
        // Confirmed-member counting alone can't see two siblings competing for the last seat in one
        // submission (neither is BActive=1 yet), so they would both pass. The caller tallies seats it
        // has handed out this batch and passes the running count here so the 2nd sibling sees it taken.
        if (reg.BActive == true) return true;
        if (reg.AssignedTeamId is not { } teamId) return true;

        var maxCount = await _context.Teams
            .Where(t => t.TeamId == teamId)
            .Select(t => t.MaxCount)
            .FirstOrDefaultAsync(cancellationToken);
        if (maxCount <= 0) return true;

        var confirmedMembers = await _context.Registrations.CountAsync(
            r => r.AssignedTeamId == teamId
              && r.RegistrationId != reg.RegistrationId
              && r.RoleId == RoleConstants.Player
              && r.BActive == true,
            cancellationToken);
        return confirmedMembers + reservedInBatch < maxCount;
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
                FeeTotal = r.FeeTotal,
                FeeDiscount = r.FeeDiscount,
                FeeDiscountMp = r.FeeDiscountMp,
                FeeLatefee = r.FeeLatefee
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
                    StreetAddress = r.User != null ? r.User.StreetAddress : null,
                    City = r.User != null ? r.User.City : null,
                    State = r.User != null ? r.User.State : null,
                    PostalCode = r.User != null ? r.User.PostalCode : null,
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
                StreetAddress = r.User != null ? r.User.StreetAddress : null,
                City = r.User != null ? r.User.City : null,
                State = r.User != null ? r.User.State : null,
                PostalCode = r.User != null ? r.User.PostalCode : null,
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

    public async Task<List<RegSaverPolicyInfo>> GetRegSaverPoliciesAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.RegsaverPolicyId != null)
            .OrderByDescending(r => r.BActive == true)
            .ThenByDescending(r => r.RegsaverPolicyIdCreateDate)
            .Select(r => new RegSaverPolicyInfo
            {
                PolicyId = r.RegsaverPolicyId!,
                PolicyCreateDate = r.RegsaverPolicyIdCreateDate,
                PlayerName = r.User != null ? (r.User.FirstName + " " + r.User.LastName).Trim() : null,
                TeamName = r.AssignedTeam != null ? r.AssignedTeam.TeamName : null
            })
            .ToListAsync(cancellationToken);
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
                JobId = r.JobId,
                BWaiverSigned3 = r.BWaiverSigned3
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Registrations?> GetByIdAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations.FindAsync(registrationId);
    }

    public async Task<Registrations?> GetByAdnSubscriptionIdAsync(string adnSubscriptionId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.AdnSubscriptionId == adnSubscriptionId, cancellationToken);
    }

    public async Task<Registrations?> GetByInvoiceAisAsync(
        int customerAi, int jobAi, int registrationAi, CancellationToken cancellationToken = default)
    {
        // Reverse of GetRegistrationWithInvoiceDataAsync: given the three AIs that make up an
        // AdnInvoiceNo (customer_job_registration), resolve the registration the charge belongs to.
        // AsNoTracking — read-only; the orphan sweep only reports, it never writes off this lookup.
        // SingleOrDefault — the AI triple is unique, so >1 match is data corruption and should throw.
        return await (
            from r in _context.Registrations
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            where r.RegistrationAi == registrationAi && j.JobAi == jobAi && c.CustomerAi == customerAi
            select r
        ).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
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
                && t.Agegroup!.AgegroupName != null
                && !t.Agegroup!.AgegroupName.Contains("WAITLIST")
                && !t.Agegroup!.AgegroupName.Contains("DROPPED"))
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
            registration.PaidTotal = totals?.PaidTotal ?? 0;
            // FeeTotal/OwedTotal derive from the summed components above. FeeMath is linear, so
            // RecalcTotals(sum of components) == the old sum of each team's stored total/owed.
            registration.RecalcTotals();
            registration.Modified = DateTime.Now;
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

    public async Task<List<Registrations>> GetActivePlayerRegistrationsByJobAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .Where(r => r.JobId == jobId
                && r.BActive == true
                && r.RoleId == RoleConstants.Player
                && r.AssignedTeamId.HasValue)
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
            // Derive FeeTotal + OwedTotal from the zeroed components. A player who already paid
            // toward this now-dropped team is owed that money back, so OwedTotal becomes
            // -PaidTotal (OVER PAID / refund owed) rather than being hidden as a flat 0.
            reg.RecalcTotals();
            reg.Modified = DateTime.Now;
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

        // Batch-load player counts per team (players only, single query)
        var teamIds = teamRows.Select(t => t.TeamId).ToList();
        var playerCounts = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.AssignedTeamId != null && teamIds.Contains(r.AssignedTeamId.Value) && r.BActive == true && r.RoleId == RoleConstants.Player)
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

    // Unpaged, ids-only projection of the same filtered query the grid runs. This is the recipient
    // source of truth for the batch-email engine when the caller sends no explicit id list.
    public async Task<List<Guid>> GetMatchingRegistrationIdsAsync(
        Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default)
    {
        var query = await BuildFilteredQueryAsync(jobId, request, ct);
        return await query.Select(r => r.RegistrationId).ToListAsync(ct);
    }

    // Composes the full filtered, AsNoTracking IQueryable for a search request (JobId scope + every
    // optional filter, including the RoleId-sentinel / Vertical Insure / USLax / ARB Health inline
    // side-queries). Shared by SearchAsync (grid pipeline) and GetMatchingRegistrationIdsAsync
    // (recipient resolution) so the money filters live in exactly one place.
    private async Task<IQueryable<Registrations>> BuildFilteredQueryAsync(
        Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default)
    {
        var query = _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId);

        // Explicit registration ID list (action-lookup caller pre-computed the candidate set,
        // e.g. ARB CC expiring this month). AND-combined with any other filter supplied.
        if (request.RegistrationIds is { Count: > 0 })
        {
            var ids = request.RegistrationIds;
            query = query.Where(r => ids.Contains(r.RegistrationId));
        }

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
            // Agegroup/division resolve through the team (AssignedTeam.AgegroupId / .DivId);
            // Registrations.AssignedAgegroupId / .AssignedDivId are obsolete.
            query = query.Where(r =>
                (hasTeamIds && r.AssignedTeamId != null && request.TeamIds!.Contains(r.AssignedTeamId.Value)) ||
                (hasAgegroupIds && r.AssignedTeam != null && request.AgegroupIds!.Contains(r.AssignedTeam.AgegroupId)) ||
                (hasDivisionIds && r.AssignedTeam != null && r.AssignedTeam.DivId != null && request.DivisionIds!.Contains(r.AssignedTeam.DivId.Value))
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
            query = query.Where(r => r.RegistrationTs >= request.RegDateFrom.Value.Date);
        if (request.RegDateTo.HasValue)
        {
            // Include the full "To" day by comparing against the next day's midnight.
            var toExclusive = request.RegDateTo.Value.Date.AddDays(1);
            query = query.Where(r => r.RegistrationTs < toExclusive);
        }

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

        // ── Vertical Insure filters ──
        // Guarded by per-category job config so the filter is a no-op on jobs that
        // don't offer that VI product (prevents returning "everyone" on non-VI jobs).
        // Each filter implicitly constrains role (Player / ClubRep).
        if (request.HasVIPlayerInsurance.HasValue || request.HasVITeamInsurance.HasValue)
        {
            var jobFlags = await _context.Jobs
                .AsNoTracking()
                .Where(j => j.JobId == jobId)
                .Select(j => new
                {
                    OffersPlayerVI = j.BOfferPlayerRegsaverInsurance ?? false,
                    OffersTeamVI = j.BOfferTeamRegsaverInsurance ?? false
                })
                .FirstOrDefaultAsync(ct);

            if (request.HasVIPlayerInsurance.HasValue && jobFlags?.OffersPlayerVI == true)
            {
                var wantAccepted = request.HasVIPlayerInsurance.Value;
                query = query.Where(r =>
                    r.RoleId == RoleConstants.Player
                    && r.BActive == true
                    && (wantAccepted ? r.RegsaverPolicyId != null : r.RegsaverPolicyId == null));
            }

            if (request.HasVITeamInsurance.HasValue && jobFlags?.OffersTeamVI == true)
            {
                var wantAccepted = request.HasVITeamInsurance.Value;
                // Team VI is accepted-per-team. At the club rep level:
                //   accepted = every active team owned by the rep has ViPolicyClubRepRegId set
                //   not accepted = at least one active team owned by the rep is missing it
                query = query.Where(r =>
                    r.RoleId == RoleConstants.ClubRep
                    && r.BActive == true
                    && (wantAccepted
                        ? !_context.Teams.Any(t =>
                            t.JobId == jobId
                            && t.ClubrepRegistrationid == r.RegistrationId
                            && t.Active == true
                            && t.ViPolicyClubRepRegId == null)
                        : _context.Teams.Any(t =>
                            t.JobId == jobId
                            && t.ClubrepRegistrationid == r.RegistrationId
                            && t.Active == true
                            && t.ViPolicyClubRepRegId == null)));
            }
        }

        // ── USLax Membership filter ──
        // Targets active Player registrations whose USLax membership does not cover
        // the job's validation window:
        //   SportAssnIdexpDate IS NULL              → on-record expiry missing
        //   SportAssnIdexpDate < UslaxNumberValidThroughDate → expires before job cutoff
        // Guarded ONLY by the job having UslaxNumberValidThroughDate set — sport is
        // intentionally not checked so non-Lacrosse jobs can opt into USLax-style
        // validation by configuring the date. Filter is a no-op when the date is
        // unset to avoid returning "everyone" on jobs that didn't opt in.
        if (!string.IsNullOrEmpty(request.UsLaxMembershipStatus))
        {
            var validThrough = await _context.Jobs
                .AsNoTracking()
                .Where(j => j.JobId == jobId)
                .Select(j => j.UslaxNumberValidThroughDate)
                .FirstOrDefaultAsync(ct);

            if (validThrough.HasValue && request.UsLaxMembershipStatus == "expired")
            {
                var cutoff = validThrough.Value;
                query = query.Where(r =>
                    r.RoleId == RoleConstants.Player
                    && r.BActive == true
                    && (r.SportAssnIdexpDate == null || r.SportAssnIdexpDate < cutoff));
            }
        }

        // ── ARB Health filter ──
        // Targets registrations with an ARB subscription that is either:
        //   "behind-active"  = subscription active or suspended, balance owed
        //   "behind-expired" = subscription expired or terminated, balance owed
        // Both are implicitly active-only to avoid reaching out to inactive registrants.
        // Guarded by the job's ARB flag so a non-ARB job ignores the filter.
        if (!string.IsNullOrEmpty(request.ArbHealthStatus))
        {
            var offersArb = await _context.Jobs
                .AsNoTracking()
                .Where(j => j.JobId == jobId)
                .Select(j => j.AdnArb ?? false)
                .FirstOrDefaultAsync(ct);

            if (offersArb)
            {
                var behindActive = new[] { "active", "suspended" };
                var behindExpired = new[] { "expired", "terminated" };
                var targetStatuses = request.ArbHealthStatus switch
                {
                    "behind-active" => behindActive,
                    "behind-expired" => behindExpired,
                    _ => Array.Empty<string>()
                };

                if (targetStatuses.Length > 0)
                {
                    query = query.Where(r =>
                        r.BActive == true
                        && r.OwedTotal > 0
                        && r.AdnSubscriptionStatus != null
                        && targetStatuses.Contains(r.AdnSubscriptionStatus));
                }
            }
        }

        return query;
    }

    public async Task<RegistrationSearchResponse> SearchAsync(
        Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default)
    {
        var query = await BuildFilteredQueryAsync(jobId, request, ct);

        // Compute count + aggregates BEFORE paging (they always span the FULL match, never the page)
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

        // Project to DTO + raw ARB schedule fields for post-materialization paymentScheduled compute.
        var projected = query.Select(r => new
        {
            Dto = new RegistrationSearchResultDto
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
            },
            RoleId = r.RoleId,
            AdnSubId = r.AdnSubscriptionId,
            AdnStatus = r.AdnSubscriptionStatus,
            AdnStart = r.AdnSubscriptionStartDate,
            AdnInterval = r.AdnSubscriptionIntervalLength,
            AdnOccurrences = r.AdnSubscriptionBillingOccurences
        });

        // Sort: honor request SortField/SortDir (grid sends camelCase DTO field names), default
        // LastName, FirstName ascending. Every branch ends with a RegistrationId tiebreaker so the
        // total order is deterministic — REQUIRED for stable server-side paging (ties must not
        // reshuffle between page fetches, or rows could duplicate/skip across pages). The computed
        // Assignment column is not DB-backed, so it falls through to the default sort.
        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var sortField = request.SortField?.ToLowerInvariant();
        var ordered = sortField switch
        {
            "firstname" => desc
                ? projected.OrderByDescending(r => r.Dto.FirstName).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.FirstName).ThenBy(r => r.Dto.RegistrationId),
            "rolename" => desc
                ? projected.OrderByDescending(r => r.Dto.RoleName).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.RoleName).ThenBy(r => r.Dto.RegistrationId),
            "registered" or "registrationts" => desc
                ? projected.OrderByDescending(r => r.Dto.RegistrationTs).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.RegistrationTs).ThenBy(r => r.Dto.RegistrationId),
            "phone" => desc
                ? projected.OrderByDescending(r => r.Dto.Phone).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.Phone).ThenBy(r => r.Dto.RegistrationId),
            "dob" => desc
                ? projected.OrderByDescending(r => r.Dto.Dob).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.Dob).ThenBy(r => r.Dto.RegistrationId),
            "position" => desc
                ? projected.OrderByDescending(r => r.Dto.Position).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.Position).ThenBy(r => r.Dto.RegistrationId),
            "paidtotal" => desc
                ? projected.OrderByDescending(r => r.Dto.PaidTotal).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.PaidTotal).ThenBy(r => r.Dto.RegistrationId),
            "owedtotal" => desc
                ? projected.OrderByDescending(r => r.Dto.OwedTotal).ThenBy(r => r.Dto.RegistrationId)
                : projected.OrderBy(r => r.Dto.OwedTotal).ThenBy(r => r.Dto.RegistrationId),
            "lastname" when desc
                => projected.OrderByDescending(r => r.Dto.LastName).ThenByDescending(r => r.Dto.FirstName).ThenBy(r => r.Dto.RegistrationId),
            _ => projected.OrderBy(r => r.Dto.LastName).ThenBy(r => r.Dto.FirstName).ThenBy(r => r.Dto.RegistrationId)
        };

        // Server-side paging: slice only when BOTH Page and PageSize are supplied. Absent ⇒ full set
        // (mobile lookup, ARB card-expiring lookup, export-all, and criteria email resolution rely
        // on the unpaged behavior).
        var paged = request.Page is int page and > 0 && request.PageSize is int size and > 0
            ? ordered.Skip((page - 1) * size).Take(size)
            : ordered;

        var rows = await paged.ToListAsync(ct);

        // Bulk-fetch team ARB info for all club rep rows with an outstanding balance —
        // a rep aggregate row is only "scheduled" when ALL its owing teams are themselves
        // on healthy ARB-Trial subs. One query covers the whole result set.
        var clubRepIds = rows
            .Where(r => r.RoleId == RoleConstants.ClubRep && r.Dto.OwedTotal > 0)
            .Select(r => r.Dto.RegistrationId)
            .Distinct()
            .ToList();

        var teamArbByRep = new Dictionary<Guid, List<(string? SubId, string? Status, DateTime? Start, int? Interval, int? Occurrences)>>();
        if (clubRepIds.Count > 0)
        {
            var teamRows = await _context.Teams
                .AsNoTracking()
                .Where(t => t.ClubrepRegistrationid != null
                    && clubRepIds.Contains(t.ClubrepRegistrationid.Value)
                    && (t.OwedTotal ?? 0m) > 0m)
                .Select(t => new
                {
                    RepId = t.ClubrepRegistrationid!.Value,
                    AdnSubId = t.AdnSubscriptionId,
                    AdnStatus = t.AdnSubscriptionStatus,
                    AdnStart = t.AdnSubscriptionStartDate,
                    AdnInterval = t.AdnSubscriptionIntervalLength,
                    AdnOccurrences = t.AdnSubscriptionBillingOccurences
                })
                .ToListAsync(ct);

            teamArbByRep = teamRows
                .GroupBy(t => t.RepId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(t => (
                        SubId: t.AdnSubId,
                        Status: t.AdnStatus,
                        Start: t.AdnStart,
                        Interval: t.AdnInterval,
                        Occurrences: t.AdnOccurrences)).ToList());
        }

        var today = DateTime.Today;
        var results = new List<RegistrationSearchResultDto>(rows.Count);

        // Post-materialization: phones + assignment + paymentScheduled compute.
        foreach (var row in rows)
        {
            var dto = row.Dto;
            var parts = new[] { dto.ClubRepClubName, dto.AgegroupName, dto.TeamName }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            // If no team assignment, fall back to registration's own ClubName (club reps)
            var assignment = parts.Any()
                ? string.Join(" ", parts)
                : !string.IsNullOrWhiteSpace(dto.ClubName) ? dto.ClubName : null;

            var (scheduled, nextDate) = (false, (DateTime?)null);
            if (dto.OwedTotal > 0m)
            {
                if (row.RoleId == RoleConstants.Player)
                {
                    // Player ARB: month-based occurrence schedule on the registration row itself.
                    (scheduled, nextDate) = ArbScheduleHelper.ComputeMonthBasedSchedule(
                        row.AdnSubId, row.AdnStatus, row.AdnStart, row.AdnInterval, row.AdnOccurrences, today);
                }
                else if (row.RoleId == RoleConstants.ClubRep
                    && teamArbByRep.TryGetValue(dto.RegistrationId, out var teamProbes)
                    && teamProbes.Count > 0)
                {
                    // Rep aggregate: scheduled iff every owing team is itself on a healthy ARB-Trial sub.
                    // NextChargeDate left null — teams may stagger; per-team detail lives in the team panel.
                    scheduled = teamProbes.All(t =>
                        ArbScheduleHelper.ComputeDayBasedSchedule(
                            t.SubId, t.Status, t.Start, t.Interval, t.Occurrences, today).PaymentScheduled);
                }
            }

            results.Add(dto with
            {
                Phone = dto.Phone.FormatPhone(),
                Assignment = assignment,
                PaymentScheduled = scheduled,
                NextChargeDate = nextDate
            });
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
            .Where(r => r.AssignedTeam != null)
            .Join(_context.Agegroups, r => r.AssignedTeam!.AgegroupId, ag => ag.AgegroupId, (r, ag) => ag)
            .GroupBy(ag => new { ag.AgegroupId, ag.AgegroupName })
            .OrderBy(g => g.Key.AgegroupName)
            .Select(g => new FilterOption { Value = g.Key.AgegroupId.ToString(), Text = g.Key.AgegroupName ?? "", Count = g.Count() })
            .ToListAsync(ct);

        var divisions = await baseQuery
            .Where(r => r.AssignedTeam != null && r.AssignedTeam.DivId != null)
            .Join(_context.Divisions, r => r.AssignedTeam!.DivId, d => d.DivId, (r, d) => d)
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

        // Gender options are intentionally NOT default-checked. Pre-selecting all
        // genders silently excluded registrants with no/null gender (e.g. club reps,
        // walk-ups) from the default search. Unselected = no gender filter applied,
        // so everyone is returned until an admin deliberately narrows by gender.
        var genders = await baseQuery
            .Where(r => r.BActive == true && r.User != null && r.User.Gender != null)
            .GroupBy(r => r.User!.Gender!)
            .OrderBy(g => g.Key)
            .Select(g => new FilterOption { Value = g.Key, Text = g.Key, Count = g.Count() })
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

        // Resolve the role-appropriate profile template. Player roles use the flat
        // PlayerProfileMetadataJson (unchanged); adult roles (coach/Staff/Referee/Recruiter) use the
        // flat sub-slice of the role-keyed AdultProfileMetadataJson; roles with no template (Club Rep,
        // etc.) get null so the panel keeps its legacy read-only list. templateDbColumns are the
        // columns the form actually collects — unioned into the projection below so the value read is
        // template-driven (e.g. a coach's BBgcheck is no longer dropped for being off the static set).
        string? resolvedMetadataJson;
        List<string> templateDbColumns;
        var adultRoleKey = AdultMetadataRoleResolver.KeyForRoleId(reg.RoleId);
        if (adultRoleKey != null)
        {
            (resolvedMetadataJson, templateDbColumns) = SliceAdultRoleTemplate(reg.Job?.AdultProfileMetadataJson, adultRoleKey);
        }
        else if (reg.RoleId == RoleConstants.Player)
        {
            resolvedMetadataJson = reg.Job?.PlayerProfileMetadataJson;
            templateDbColumns = ExtractDbColumns(resolvedMetadataJson);
        }
        else
        {
            resolvedMetadataJson = null;
            templateDbColumns = new List<string>();
        }

        // Coach/Staff: decode the codified SpecialRequests team-request blob so the panel can show it
        // read-only. Never the raw JSON. The human note and the requested-team labels are surfaced as
        // separate, resolved fields (the raw blob stays owned by the approval queue).
        string? coachRequestNote = null;
        List<string>? coachRequestedTeams = null;
        if (adultRoleKey == AdultMetadataRoleResolver.UnassignedAdult)
        {
            var record = AdultTeamRequestData.Parse(reg.SpecialRequests);
            coachRequestNote = string.IsNullOrWhiteSpace(record.Note) ? null : record.Note;

            var requestedIds = record.GetRequestedTeamIds().ToList();
            if (requestedIds.Count > 0)
            {
                // Resolve ids to CURRENT labels (rename-proof). Club via the canonical
                // Teams.ClubrepRegistrationid -> Registrations.ClubName route, left-joined
                // because that FK is nullable (club/league teams may carry no club rep).
                var labelParts = await (
                    from t in _context.Teams
                    join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                    join rCR in _context.Registrations on t.ClubrepRegistrationid equals rCR.RegistrationId into crj
                    from rCR in crj.DefaultIfEmpty()
                    where requestedIds.Contains(t.TeamId)
                    orderby ag.AgegroupName, t.TeamName
                    select new { Club = rCR != null ? rCR.ClubName : null, Age = ag.AgegroupName, Team = t.TeamName })
                    .AsNoTracking()
                    .ToListAsync(ct);

                coachRequestedTeams = labelParts
                    .Select(p => string.IsNullOrWhiteSpace(p.Club)
                        ? $"{p.Age}: {p.Team}"
                        : $"{p.Club}: {p.Age}: {p.Team}")
                    .ToList();
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

        // Union the active template's declared columns so the projection reads exactly what the form
        // collects (columns not present on the entity are silently skipped by the reflection below).
        foreach (var col in templateDbColumns)
            profileProps.Add(col);

        var regType = typeof(Registrations);
        foreach (var propName in profileProps)
        {
            var prop = regType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null)
            {
                var val = prop.GetValue(reg);
                // DateTime values must ship as ISO yyyy-MM-dd so HTML <input type="date">
                // can round-trip them. Default DateTime.ToString() produces culture-local
                // format (e.g. "9/30/2026 12:00:00 AM") which the date input silently rejects.
                profileValues[propName] = val switch
                {
                    DateTime dt => dt.ToString("yyyy-MM-dd"),
                    _ => val?.ToString()
                };
            }
        }

        // For a coach (UnassignedAdult) the SpecialRequests column holds the codified
        // team-request JSON blob, not a free-text answer — surfacing it here leaked the
        // raw "{"teams":[...]}" into the panel's "Special Requests" row. The human note is
        // already decoded into CoachRequestNote (its own read-only section), so drop the raw
        // value. Mirrors SliceAdultRoleTemplate, which drops the same field from the editable
        // metadata for this role.
        if (adultRoleKey == AdultMetadataRoleResolver.UnassignedAdult)
            profileValues.Remove(nameof(Registrations.SpecialRequests));

        // Build accounting records
        var accountingRecords = reg.RegistrationAccounting
            .OrderByDescending(a => a.Createdate)
            .Select(a => new AccountingRecordDto
            {
                AId = a.AId,
                TeamId = a.TeamId,
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

        // FK scope for deletability: count ALL teams (active or inactive) pointing at this registration.
        var clubRepTeamCount = await _context.Teams
            .CountAsync(t => t.ClubrepRegistrationid == reg.RegistrationId, ct);

        // Project the stored ARB snapshot from the Registrations.AdnSubscription* columns so the
        // detail panel can show subscription status in every environment — the live ADN lookup only
        // works in Production (a prod subscription id can't be resolved against the sandbox gateway).
        SubscriptionDetailDto? storedSubscription = null;
        if (!string.IsNullOrWhiteSpace(reg.AdnSubscriptionId))
        {
            var perOccurrence = reg.AdnSubscriptionAmountPerOccurence ?? 0m;
            var occurrences = reg.AdnSubscriptionBillingOccurences ?? 0;
            var intervalLength = reg.AdnSubscriptionIntervalLength ?? 1;
            storedSubscription = new SubscriptionDetailDto
            {
                SubscriptionId = reg.AdnSubscriptionId!,
                Status = reg.AdnSubscriptionStatus ?? "unknown",
                PerOccurrenceAmount = perOccurrence,
                TotalOccurrences = occurrences,
                TotalAmount = perOccurrence * occurrences,
                StartDate = reg.AdnSubscriptionStartDate ?? DateTime.MinValue,
                IntervalLabel = intervalLength == 1 ? "every month" : $"every {intervalLength} months"
            };
        }

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
            FamilyUserId = reg.FamilyUserId,
            ProfileMetadataJson = resolvedMetadataJson,
            CoachRequestNote = coachRequestNote,
            CoachRequestedTeams = coachRequestedTeams,
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
            StoredSubscription = storedSubscription,
            RegistrationDate = reg.RegistrationTs,
            ModifiedDate = reg.Modified,
            AccountingRecords = accountingRecords,
            IsClubRep = isClubRep,
            ClubRepTeamCount = clubRepTeamCount
        };
    }

    /// <summary>
    /// Slice one role out of the role-keyed <c>AdultProfileMetadataJson</c> and re-emit it as the flat
    /// <c>{"fields":[...]}</c> shape the detail panel's parser expects. For the coach block
    /// (<c>UnassignedAdult</c>, which also backs Staff) the <c>SpecialRequests</c> field is dropped —
    /// that column holds the codified team-request blob, never an editable free-text answer. Returns the
    /// declared dbColumns alongside so the value projection can read exactly the collected columns.
    /// </summary>
    private static (string? MetadataJson, List<string> DbColumns) SliceAdultRoleTemplate(string? adultJson, string roleKey)
    {
        var dbColumns = new List<string>();
        if (string.IsNullOrWhiteSpace(adultJson)) return (null, dbColumns);
        try
        {
            if (JsonNode.Parse(adultJson) is not JsonObject root
                || root[roleKey] is not JsonObject roleObj
                || roleObj["fields"] is not JsonArray fields)
                return (null, dbColumns);

            var stripSpecialRequests = roleKey == AdultMetadataRoleResolver.UnassignedAdult;
            var kept = new JsonArray();
            foreach (var f in fields)
            {
                if (f is not JsonObject fo) continue;
                var col = FieldColumn(fo);
                if (stripSpecialRequests
                    && string.Equals(col, nameof(Registrations.SpecialRequests), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(col)) dbColumns.Add(col!);
                kept.Add(fo.DeepClone());
            }
            return (new JsonObject { ["fields"] = kept }.ToJsonString(), dbColumns);
        }
        catch (JsonException)
        {
            return (null, dbColumns);
        }
    }

    /// <summary>Collect the backing dbColumns from a flat player template (<c>{"fields":[...]}</c> or a
    /// bare fields array). Used to make the value projection template-driven for players too.</summary>
    private static List<string> ExtractDbColumns(string? flatJson)
    {
        var dbColumns = new List<string>();
        if (string.IsNullOrWhiteSpace(flatJson)) return dbColumns;
        try
        {
            var fields = JsonNode.Parse(flatJson) switch
            {
                JsonArray arr => arr,
                JsonObject obj when obj["fields"] is JsonArray fa => fa,
                _ => null
            };
            if (fields == null) return dbColumns;
            foreach (var f in fields)
            {
                if (f is not JsonObject fo) continue;
                var col = FieldColumn(fo);
                if (!string.IsNullOrWhiteSpace(col)) dbColumns.Add(col!);
            }
        }
        catch (JsonException) { /* malformed metadata → no extra columns */ }
        return dbColumns;
    }

    /// <summary>A metadata field's backing column: <c>dbColumn</c> if present, else <c>name</c>.</summary>
    private static string? FieldColumn(JsonObject field)
    {
        var dbColumn = field["dbColumn"]?.GetValue<string>();
        return !string.IsNullOrWhiteSpace(dbColumn) ? dbColumn : field["name"]?.GetValue<string>();
    }

    public async Task UpdateRegistrationProfileAsync(
        Guid jobId, string userId, UpdateRegistrationProfileRequest request, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .Include(r => r.Role)
            .Where(r => r.RegistrationId == request.RegistrationId && r.JobId == jobId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Registration not found or does not belong to this job.");

        // Coach persona = Unassigned Adult OR promoted Staff; both carry the codified SpecialRequests blob.
        var isCoachPersona = reg.Role?.Name == RoleConstants.Names.UnassignedAdultName
                          || reg.Role?.Name == RoleConstants.Names.StaffName;

        var regType = typeof(Registrations);
        foreach (var (key, value) in request.ProfileValues)
        {
            // USA Lacrosse expiry is USLax-authoritative for EVERY role: the generic profile editor
            // never writes it. Its sole writer is the revalidate path (UpdateSportAssnIdExpDateAsync),
            // which records only a definitive USLax hit — so an admin can never assert an unverified date.
            if (string.Equals(key, nameof(Registrations.SportAssnIdexpDate), StringComparison.OrdinalIgnoreCase))
                continue;

            // Immutability: a coach's/Staff's SpecialRequests is an append-only codified record managed
            // by the approval queue — the generic profile editor must never overwrite it.
            if (isCoachPersona
                && string.Equals(key, nameof(Registrations.SpecialRequests), StringComparison.OrdinalIgnoreCase))
                continue;

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

        reg.Modified = DateTime.Now;
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
        family.Modified = DateTime.Now;
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

        // NOT the UserManager path — this is SqlDbContext.AspNetUsers, so nothing normalizes for us.
        // `user.Email = …` alone leaves NormalizedEmail stale, and NormalizedEmail is the column
        // FindByEmailAsync searches for forgot-password. See AspNetUserEmail.
        AspNetUserEmail.Set(user, demo.Email);

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

        // Same lane, same rule: the family LOGIN is the account a parent signs in with, so a stale
        // NormalizedEmail here is precisely the account that cannot reset its own password.
        AspNetUserEmail.Set(user, demo.Email);

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
                r.RegistrationTs,
                r.UniformNo
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
            RegistrationTs = r.RegistrationTs,
            UniformNo = r.UniformNo
        }).ToList();
    }

    public async Task<List<TSIC.Contracts.Dtos.MyRoster.MyRosterPlayerDto>> GetMyRosterByTeamIdAsync(
        Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        // Parent contact comes from Families via the FamilyUser nav (FK_Registrations_Families);
        // EF emits a LEFT JOIN, so adults/staff with no family account simply project nulls.
        var raw = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.AssignedTeamId == teamId && r.JobId == jobId && (r.BActive ?? true))
            .Select(r => new
            {
                r.RegistrationId,
                LastName = r.User != null ? r.User.LastName ?? "" : "",
                FirstName = r.User != null ? r.User.FirstName ?? "" : "",
                RoleName = r.Role != null ? r.Role.Name ?? "" : "",
                BActive = r.BActive ?? true,
                Email = r.User != null ? r.User.Email : null,
                Cellphone = r.User != null ? r.User.Cellphone : null,
                GradYearRaw = r.GradYear,
                r.Position,
                r.UniformNo,
                Gender = r.User != null ? r.User.Gender : null,
                DobRaw = r.User != null ? r.User.Dob : null,
                MomFirstName = r.FamilyUser != null ? r.FamilyUser.MomFirstName : null,
                MomLastName = r.FamilyUser != null ? r.FamilyUser.MomLastName : null,
                MomEmail = r.FamilyUser != null ? r.FamilyUser.MomEmail : null,
                MomCellphone = r.FamilyUser != null ? r.FamilyUser.MomCellphone : null,
                DadFirstName = r.FamilyUser != null ? r.FamilyUser.DadFirstName : null,
                DadLastName = r.FamilyUser != null ? r.FamilyUser.DadLastName : null,
                DadEmail = r.FamilyUser != null ? r.FamilyUser.DadEmail : null,
                DadCellphone = r.FamilyUser != null ? r.FamilyUser.DadCellphone : null,
            })
            .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
            .ToListAsync(ct);

        return raw.Select(r => new TSIC.Contracts.Dtos.MyRoster.MyRosterPlayerDto
        {
            RegistrationId = r.RegistrationId,
            PlayerName = string.IsNullOrWhiteSpace(r.LastName) && string.IsNullOrWhiteSpace(r.FirstName)
                ? "Unknown"
                : $"{r.LastName}, {r.FirstName}".Trim().TrimEnd(',').Trim(),
            RoleName = r.RoleName,
            BActive = r.BActive,
            FirstName = r.FirstName,
            LastName = r.LastName,
            Email = r.Email,
            Cellphone = r.Cellphone,
            GradYear = int.TryParse(r.GradYearRaw, out var gy) ? gy : null,
            Position = r.Position,
            UniformNo = r.UniformNo,
            Gender = r.Gender,
            Dob = r.DobRaw.HasValue ? DateOnly.FromDateTime(r.DobRaw.Value) : null,
            MomFirstName = r.MomFirstName,
            MomLastName = r.MomLastName,
            MomEmail = r.MomEmail,
            MomCellphone = r.MomCellphone,
            DadFirstName = r.DadFirstName,
            DadLastName = r.DadLastName,
            DadEmail = r.DadEmail,
            DadCellphone = r.DadCellphone,
        }).ToList();
    }

    public async Task<(bool AllowPlayer, bool AllowAdult)?> GetRosterViewFlagsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var row = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.BAllowRosterViewPlayer, j.BAllowRosterViewAdult })
            .FirstOrDefaultAsync(ct);
        return row == null ? null : (row.BAllowRosterViewPlayer, row.BAllowRosterViewAdult);
    }

    public async Task<(string? TeamName, string? AgegroupName, bool IsSystemBucket)?> GetTeamHeaderAsync(
        Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        var row = await _context.Teams
            .AsNoTracking()
            .Where(t => t.TeamId == teamId && t.JobId == jobId)
            .Select(t => new { t.TeamName, AgegroupName = t.Agegroup.AgegroupName })
            .FirstOrDefaultAsync(ct);

        // The agegroup name is already projected, so the roster gate rides along for free.
        return row == null
            ? null
            : (row.TeamName, row.AgegroupName, AgegroupConstants.IsSystemBucket(row.AgegroupName));
    }

    public async Task<List<SwapperPlayerDto>> GetUnassignedAdultsAsync(Guid jobId, CancellationToken ct = default)
    {
        var rows = await _context.Registrations
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
                // SpecialRequests now holds codified JSON; surface a human summary instead of raw JSON.
                Requests = r.SpecialRequests,
                PrevCoach = null,
                FeeBase = r.FeeBase,
                FeeTotal = r.FeeTotal,
                OwedTotal = r.OwedTotal,
                RegistrationTs = r.RegistrationTs
            })
            .OrderBy(p => p.PlayerName)
            .ToListAsync(ct);

        // Render the codified request blob → a readable "Requested N team(s)" + note line.
        // (The dedicated coach-approval queue resolves team labels; this is the legacy
        // Roster Swapper "Requests" column, which only needs to avoid showing raw JSON.)
        return rows
            .Select(p =>
            {
                var req = AdultTeamRequestData.Parse(p.Requests);
                var parts = new List<string>();
                var requestedCount = req.GetRequestedTeamIds().Count;
                if (requestedCount > 0)
                    parts.Add($"Requested {requestedCount} team(s)");
                if (!string.IsNullOrWhiteSpace(req.Note))
                    parts.Add(req.Note!.Trim());
                return p with { Requests = parts.Count > 0 ? string.Join(" — ", parts) : null };
            })
            .ToList();
    }

    /// <summary>Display parts for one team — the single source for coach/queue team labels.</summary>
    private sealed record TeamLabelParts(string Club, string Age, string Div, string Team)
    {
        /// <summary>Approval-queue label — a clean human string, empty parts dropped:
        /// <c>"{Club}: {Team} · {Age}"</c> (e.g. "ASL: Long Island Red 2028 · 2028", or
        /// "Long Island Red 2028 · 2028" with no club). Replaces the old colon-soup
        /// <c>club:age:div:team</c> that rendered leading/empty colons in the queue UI.</summary>
        public string QueueLabel()
        {
            var prefix = string.IsNullOrWhiteSpace(Club) ? "" : $"{Club}: ";
            var ageCtx = string.IsNullOrWhiteSpace(Age) ? "" : $" · {Age}";
            return $"{prefix}{Team}{ageCtx}";
        }

        /// <summary>Minted-anchor Assignment label: <c>"{club}: {age}:{team}"</c> (club prefix when present).</summary>
        public string AssignmentLabel() =>
            (string.IsNullOrWhiteSpace(Club) ? "" : $"{Club}: ") + $"{Age}:{Team}";
    }

    /// <summary>
    /// Shared team-label lookup: club / agegroup / div / team name parts for a set of teams in a
    /// job. One join, reused by the approval queue (<see cref="GetUnassignedAdultQueueAsync"/>) and
    /// the request-record seed (<see cref="SeedAdultRequestRecordsAsync"/>) so the team-label
    /// projection lives in one place; each caller formats the parts to its own shape.
    /// </summary>
    private async Task<Dictionary<Guid, TeamLabelParts>> GetTeamLabelPartsAsync(
        Guid jobId, IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        if (teamIds.Count == 0) return new();
        return (await _context.Teams
            .AsNoTracking()
            .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
            .Select(t => new
            {
                t.TeamId,
                Club = t.ClubrepRegistration!.ClubName ?? "",
                Age = t.Agegroup.AgegroupName ?? "",
                Div = t.Div == null ? "" : (t.Div.DivName ?? ""),
                Team = t.TeamName ?? ""
            })
            .ToListAsync(ct))
            .ToDictionary(t => t.TeamId, t => new TeamLabelParts(t.Club, t.Age, t.Div, t.Team));
    }

    public async Task<List<UnassignedAdultQueueRowDto>> GetUnassignedAdultQueueAsync(Guid jobId, CancellationToken ct = default)
    {
        // 1. ACTIVE unassigned adults for this job: identity + raw codified SpecialRequests.
        //    Every active coach is listed — nothing auto-retires; Deny (bActive=0) is the only exit.
        var coaches = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.BActive == true
                && r.Role!.Name == RoleConstants.Names.UnassignedAdultName)
            .Select(r => new
            {
                r.RegistrationId,
                r.UserId,
                FirstName = r.User != null ? r.User.FirstName : null,
                LastName = r.User != null ? r.User.LastName : null,
                Email = r.User != null ? r.User.Email : null,
                Cellphone = r.User != null ? r.User.Cellphone : null,
                City = r.User != null ? r.User.City : null,
                State = r.User != null ? r.User.State : null,
                r.ClubName,
                r.SpecialRequests,
                r.SportAssnId,
                r.SportAssnIdexpDate,
                r.RegistrationTs
            })
            .ToListAsync(ct);

        if (coaches.Count == 0) return new List<UnassignedAdultQueueRowDto>();

        // 2. Parse codified requests in memory (EF can't translate the JSON parse).
        var parsed = coaches
            .Select(c => new { Coach = c, Req = AdultTeamRequestData.Parse(c.SpecialRequests) })
            .ToList();

        // Every team in any coach's record (asks ∪ grants), for label resolution.
        var allRecordedTeamIds = parsed
            .SelectMany(p => p.Req.GetAllTeamIds())
            .Distinct()
            .ToList();

        var coachUserIds = coaches
            .Where(c => c.UserId != null)
            .Select(c => c.UserId!)
            .Distinct()
            .ToList();

        // 3. Current-job Staff rows for these coaches = their LIVE team assignments (current
        //    grants). These pre-check the dropdown; each carries its Staff registration id so an
        //    un-check can delete exactly that row (FLOW 3).
        var staffRows = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.Role!.Name == RoleConstants.Names.StaffName
                && r.UserId != null && r.AssignedTeamId != null
                && coachUserIds.Contains(r.UserId))
            .Select(r => new { r.RegistrationId, r.UserId, TeamId = r.AssignedTeamId!.Value })
            .ToListAsync(ct);

        var assignedByUser = staffRows
            .GroupBy(r => r.UserId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 4. Resolve display labels for every team we show — recorded (codified) ∪ assigned.
        var labelIds = allRecordedTeamIds.Concat(staffRows.Select(r => r.TeamId)).Distinct().ToList();
        var teamLabels = (await GetTeamLabelPartsAsync(jobId, labelIds, ct))
            .ToDictionary(kv => kv.Key, kv => kv.Value.QueueLabel());

        // 5. Prior Staff history in OTHER jobs/seasons — the lead recognition signal. Excludes
        //    THIS job: a placement here is the coach's current assignment (shown in the
        //    dropdown), not "coached before".
        var priorStaff = (await _context.Registrations
            .AsNoTracking()
            .Where(r => r.Role!.Name == RoleConstants.Names.StaffName
                && r.JobId != jobId
                && r.AssignedTeamId != null
                && r.UserId != null
                && coachUserIds.Contains(r.UserId))
            .Select(r => new
            {
                r.UserId,
                TeamName = r.AssignedTeam != null ? r.AssignedTeam.TeamName : null,
                JobName = r.Job.JobName
            })
            .ToListAsync(ct))
            .GroupBy(r => r.UserId!)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new PriorStaffAssignmentDto
                {
                    TeamName = x.TeamName ?? "",
                    JobName = x.JobName ?? ""
                }).ToList());

        // 6. Family linkage (minor signal): players in THIS job under the coach's account.
        var familyPlayers = (await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.Role!.Name == RoleConstants.Names.PlayerName
                && r.FamilyUserId != null
                && coachUserIds.Contains(r.FamilyUserId))
            .Select(r => new
            {
                r.FamilyUserId,
                PlayerName = r.User != null
                    ? ((r.User.FirstName ?? "") + " " + (r.User.LastName ?? "")).Trim()
                    : "",
                r.AssignedTeamId
            })
            .ToListAsync(ct))
            .GroupBy(p => p.FamilyUserId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 7. Assemble. RecordedTeams = the immutable JSON record (asks ∪ grants), tagged.
        //    AssignedTeams = live Staff rows (current grants). Every active coach is included.
        var result = new List<UnassignedAdultQueueRowDto>();
        foreach (var p in parsed)
        {
            var c = p.Coach;
            var userId = c.UserId;

            var recorded = p.Req.Teams
                .Select(t => new UnassignedAdultRecordedTeamDto
                {
                    TeamId = t.TeamId,
                    DisplayText = teamLabels.TryGetValue(t.TeamId, out var lbl) && lbl.Length > 0
                        ? lbl : "(team)",
                    Source = t.Src == AdultTeamRequestSource.Self ? "self" : "admin"
                })
                .ToList();

            var assigned = userId != null && assignedByUser.TryGetValue(userId, out var ar)
                ? ar.Select(a => new UnassignedAdultAssignedTeamDto
                {
                    TeamId = a.TeamId,
                    DisplayText = teamLabels.TryGetValue(a.TeamId, out var lbl) && lbl.Length > 0
                        ? lbl : "(team)",
                    StaffRegistrationId = a.RegistrationId
                }).ToList()
                : new List<UnassignedAdultAssignedTeamDto>();

            var playerName = ($"{c.LastName ?? ""}, {c.FirstName ?? ""}").Trim().TrimEnd(',').Trim();
            if (string.IsNullOrEmpty(playerName)) playerName = "Unknown";

            var prior = userId != null && priorStaff.TryGetValue(userId, out var ps)
                ? ps : new List<PriorStaffAssignmentDto>();
            var linked = userId != null && familyPlayers.TryGetValue(userId, out var fl)
                ? fl.Select(x => x.PlayerName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList()
                : new List<string>();

            result.Add(new UnassignedAdultQueueRowDto
            {
                RegistrationId = c.RegistrationId,
                PlayerName = playerName,
                ClubName = c.ClubName,
                Email = c.Email,
                Cellphone = c.Cellphone,
                City = c.City,
                State = c.State,
                RegistrationTs = c.RegistrationTs,
                Note = p.Req.Note,
                SportAssnId = c.SportAssnId,
                SportAssnIdexpDate = c.SportAssnIdexpDate,
                IdVerified = p.Req.IdVerified == true,
                PriorStaff = prior,
                LinkedPlayerNames = linked,
                RecordedTeams = recorded,
                AssignedTeams = assigned
            });
        }

        return result.OrderBy(r => r.PlayerName).ToList();
    }

    public async Task<CoachUsLaxRefDto?> GetUnassignedAdultUsLaxRefAsync(
        Guid registrationId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId && r.JobId == jobId
                && r.Role!.Name == RoleConstants.Names.UnassignedAdultName)
            .Select(r => new CoachUsLaxRefDto { UserId = r.UserId, SportAssnId = r.SportAssnId })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> UpdateUsLaxExpiryForUserInJobAsync(
        string userId, Guid jobId, DateTime? expDate, CancellationToken ct = default)
    {
        // Fan out to the anchor + every Staff grant for this coach in the job. Tracked load +
        // SaveChanges (small row count per coach) keeps it in the unit-of-work the repo owns.
        var rows = await _context.Registrations
            .Where(r => r.UserId == userId && r.JobId == jobId && r.SportAssnId != null)
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            r.SportAssnIdexpDate = expDate;
            r.Modified = DateTime.Now;
        }

        if (rows.Count > 0) await _context.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// Build Rule seed — codify pre-existing (legacy) team grants into each coach's append-only
    /// record so every LIVE Staff grant reads as "assigned" in the approval queue. Restores the
    /// invariant <c>assignedTeams ⊆ recordedTeams</c> for adults rostered before the record was
    /// codified. Keyed on the live Staff rows (<c>AssignedTeamId</c> set) — the assignment source
    /// of truth — grouped per adult, with two actions:
    /// <list type="bullet">
    /// <item><b>No anchor</b> ("Assigned, no anchor" — legacy coaches rostered without being funneled
    ///   through the firewall): MINT an active anchor carrying the Staff club label + earliest roster
    ///   date, seeded with the current grants as <see cref="AdultTeamRequestSource.Admin"/>.</item>
    /// <item><b>Active anchor</b> whose record is missing a held grant (legacy free-text record, or
    ///   drift — the <b>Combo</b> population that <see cref="AppendGrantedTeamToRecordAsync"/> never
    ///   codified because the grant predates the flow): APPEND the missing grants as
    ///   <see cref="AdultTeamRequestSource.Admin"/>. <see cref="AdultTeamRequestData.Parse"/> keeps the
    ///   legacy free-text as the note and <see cref="AdultTeamRequestData.AddTeam"/> dedups + never
    ///   downgrades a <see cref="AdultTeamRequestSource.Self"/> pick, so it is a pure append —
    ///   nothing is rewritten or lost.</item>
    /// </list>
    /// A coach whose ONLY anchor is denied (<c>bActive=0</c>) is skipped for both mint and append —
    /// never resurrected. Idempotent: a fully-codified record yields no writes (every AddTeam is a
    /// no-op), so re-running (every queue open) is a no-op once healed.
    /// </summary>
    public async Task SeedAdultRequestRecordsAsync(Guid jobId, string adminUserId, CancellationToken ct = default)
    {
        // Directly-rostered Staff in the job = the assignment source of truth, grouped by adult.
        var staffRows = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId
                && r.Role!.Name == RoleConstants.Names.StaffName
                && r.UserId != null && r.AssignedTeamId != null)
            .Select(r => new { r.UserId, TeamId = r.AssignedTeamId!.Value, r.ClubName, r.RegistrationTs })
            .ToListAsync(ct);
        if (staffRows.Count == 0) return;

        // Every UnassignedAdult anchor in the job (any status), tracked — we append into the
        // ACTIVE ones. A denied-only anchor (bActive=0) still suppresses mint so a denied coach is
        // never resurrected; denied rows are loaded but never mutated.
        var anchors = await _context.Registrations
            .Where(r => r.JobId == jobId
                && r.Role!.Name == RoleConstants.Names.UnassignedAdultName
                && r.UserId != null)
            .ToListAsync(ct);
        var activeAnchorByUser = anchors
            .Where(r => r.BActive == true)
            .GroupBy(r => r.UserId!)
            .ToDictionary(g => g.Key, g => g.First());
        var anchoredUserIds = anchors.Select(r => r.UserId!).ToHashSet();

        // Team label parts (shared lookup) → the minted anchor's Assignment field.
        var teamIds = staffRows.Select(r => r.TeamId).Distinct().ToList();
        var teamLabels = (await GetTeamLabelPartsAsync(jobId, teamIds, ct))
            .ToDictionary(kv => kv.Key, kv => kv.Value.AssignmentLabel());

        var now = DateTime.Now;
        var dirty = false;
        foreach (var grp in staffRows.GroupBy(r => r.UserId!))
        {
            var grpTeamIds = grp.Select(x => x.TeamId).Distinct().ToList();

            // Existing active anchor → fold any held grant missing from its record (legacy
            // free-text or drift) as Admin. Pure append: the note + any Self tags are preserved.
            if (activeAnchorByUser.TryGetValue(grp.Key, out var anchor))
            {
                var record = AdultTeamRequestData.Parse(anchor.SpecialRequests);
                var changed = false;
                foreach (var tid in grpTeamIds)
                    changed |= record.AddTeam(tid, AdultTeamRequestSource.Admin);
                if (changed)
                {
                    anchor.SpecialRequests = AdultTeamRequestData.Serialize(record);
                    anchor.Modified = now;
                    anchor.LebUserId = adminUserId;
                    dirty = true;
                }
                continue;
            }

            if (anchoredUserIds.Contains(grp.Key)) continue; // denied-only anchor — do not resurrect

            // No anchor at all → MINT an active anchor seeded with the current grants.
            var data = new AdultTeamRequestData();
            foreach (var tid in grpTeamIds)
                data.AddTeam(tid, AdultTeamRequestSource.Admin);

            // Carry the club label and earliest roster date from the Staff rows so the queue row
            // reads truthfully (club + how long they've been on the roster).
            var clubName = grp.Select(x => x.ClubName).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

            // Assignment = the team label(s) the coach is rostered to, joined when more than one.
            var assignment = string.Join("; ", grpTeamIds
                .Select(tid => teamLabels.TryGetValue(tid, out var l) ? l : null)
                .Where(l => !string.IsNullOrEmpty(l)));

            _context.Registrations.Add(new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                UserId = grp.Key,
                JobId = jobId,
                RoleId = RoleConstants.UnassignedAdult,
                BActive = true,
                AssignedTeamId = null,
                FamilyUserId = null,
                ClubName = clubName,
                Assignment = string.IsNullOrEmpty(assignment) ? null : assignment,
                SpecialRequests = AdultTeamRequestData.Serialize(data),
                RegistrationTs = grp.Min(x => x.RegistrationTs),
                LebUserId = adminUserId,
                Modified = now
            });
            dirty = true;
        }

        if (dirty) await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Append-on-grant: record that a director granted <paramref name="teamId"/> by adding it to
    /// the coach's append-only record as <see cref="AdultTeamRequestSource.Admin"/> (no-op if the
    /// team is already recorded — a prior <c>self</c> request is never downgraded). The JSON is
    /// never otherwise modified. Returns false if the registration isn't a valid UnassignedAdult.
    /// </summary>
    public async Task<bool> AppendGrantedTeamToRecordAsync(
        Guid registrationId, Guid jobId, Guid teamId, string adminUserId, CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .Include(r => r.Role)
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId && r.JobId == jobId, ct);

        if (reg == null || reg.Role?.Name != RoleConstants.Names.UnassignedAdultName)
            return false;

        var data = AdultTeamRequestData.Parse(reg.SpecialRequests);
        if (!data.AddTeam(teamId, AdultTeamRequestSource.Admin))
            return false; // already in the record — nothing to append

        reg.SpecialRequests = AdultTeamRequestData.Serialize(data);
        reg.Modified = DateTime.Now;
        reg.LebUserId = adminUserId;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Deny a coach outright: delete ALL their Staff rows in the job (and the device links those
    /// rows carried) and deactivate the UnassignedAdult anchor (<c>bActive=0</c>) so they drop
    /// from the queue. The immutable team record (SpecialRequests JSON) is left untouched — the
    /// history of what was requested/granted survives. Returns false if the anchor isn't found.
    /// </summary>
    public async Task<bool> DenyCoachAsync(
        Guid registrationId, Guid jobId, string adminUserId, CancellationToken ct = default)
    {
        var anchor = await _context.Registrations
            .Include(r => r.Role)
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId && r.JobId == jobId, ct);

        if (anchor == null || anchor.Role?.Name != RoleConstants.Names.UnassignedAdultName)
            return false;

        var now = DateTime.Now;

        if (anchor.UserId != null)
        {
            var staffRegs = await _context.Registrations
                .Where(r => r.JobId == jobId
                    && r.Role!.Name == RoleConstants.Names.StaffName
                    && r.UserId == anchor.UserId)
                .Select(r => r.RegistrationId)
                .ToListAsync(ct);

            if (staffRegs.Count > 0)
            {
                var deviceTeams = await _context.DeviceTeams
                    .Where(dt => dt.RegistrationId != null && staffRegs.Contains(dt.RegistrationId.Value))
                    .ToListAsync(ct);
                _context.DeviceTeams.RemoveRange(deviceTeams);

                var deviceRegIds = await _context.DeviceRegistrationIds
                    .Where(dr => staffRegs.Contains(dr.RegistrationId))
                    .ToListAsync(ct);
                _context.DeviceRegistrationIds.RemoveRange(deviceRegIds);

                var toDelete = await _context.Registrations
                    .Where(r => staffRegs.Contains(r.RegistrationId))
                    .ToListAsync(ct);
                _context.Registrations.RemoveRange(toDelete);
            }
        }

        anchor.BActive = false;
        anchor.Modified = now;
        anchor.LebUserId = adminUserId;
        await _context.SaveChangesAsync(ct);
        return true;
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

    public async Task<List<UsLaxReconciliationCandidateRow>> GetUsLaxReconciliationCandidatesAsync(Guid jobId, UsLaxMembershipRole role, CancellationToken ct = default)
    {
        var roleId = role == UsLaxMembershipRole.Coach ? RoleConstants.UnassignedAdult : RoleConstants.Player;

        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            where r.JobId == jobId
                  && r.RoleId == roleId
                  && r.BActive == true
                  && r.SportAssnId != null
                  && u.Dob != null
                  && j.Sport != null
                  && j.Sport.SportName == "Lacrosse"
            orderby u.LastName, u.FirstName
            select new UsLaxReconciliationCandidateRow
            {
                RegistrationId = r.RegistrationId,
                FirstName = u.FirstName ?? string.Empty,
                LastName = u.LastName ?? string.Empty,
                Email = u.Email,
                Dob = u.Dob,
                SportAssnId = r.SportAssnId!,
                SportAssnIdexpDate = r.SportAssnIdexpDate,
                TeamName = r.AssignedTeam != null && r.AssignedTeam.Agegroup != null
                    ? r.AssignedTeam.Agegroup.AgegroupName + ":" + r.AssignedTeam.TeamName
                    : r.AssignedTeam != null ? r.AssignedTeam.TeamName : null
            }
        ).AsNoTracking().ToListAsync(ct);
    }

    public async Task UpdateSportAssnIdExpDateAsync(Guid registrationId, DateTime newExpiryDate, CancellationToken ct = default)
    {
        // ExecuteUpdate avoids loading the whole entity into the change tracker.
        await _context.Registrations
            .Where(r => r.RegistrationId == registrationId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.SportAssnIdexpDate, newExpiryDate), ct);
    }

    public async Task<List<CampPlayerDto>> GetCampersByTeamAsync(Guid teamId, CancellationToken ct = default)
    {
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.AssignedTeamId == teamId
                  && r.BActive == true
                  && r.RoleId == RoleConstants.Player
            orderby r.GradYear, u.LastName, u.FirstName
            select new CampPlayerDto
            {
                RegistrationId = r.RegistrationId,
                FirstName = u.FirstName ?? string.Empty,
                LastName = u.LastName ?? string.Empty,
                SchoolName = r.SchoolName,
                GradYear = r.GradYear,
                Position = r.Position,
                ClubName = r.ClubName,
                DayGroup = r.DayGroup,
                NightGroup = r.NightGroup,
            })
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<bool> UpdateCampGroupsAsync(
        Guid jobId,
        Guid registrationId,
        string? dayGroup,
        string? nightGroup,
        bool updateDayGroup,
        bool updateNightGroup,
        CancellationToken ct = default)
    {
        if (!updateDayGroup && !updateNightGroup) return false;

        var reg = await _context.Registrations
            .Where(r => r.RegistrationId == registrationId && r.JobId == jobId)
            .SingleOrDefaultAsync(ct);

        if (reg is null) return false;

        if (updateDayGroup) reg.DayGroup = NormalizeBlank(dayGroup);
        if (updateNightGroup) reg.NightGroup = NormalizeBlank(nightGroup);

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> BulkUpdateCampGroupsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> registrationIds,
        string? dayGroup,
        string? nightGroup,
        bool updateDayGroup,
        bool updateNightGroup,
        CancellationToken ct = default)
    {
        if (registrationIds.Count == 0) return 0;
        if (!updateDayGroup && !updateNightGroup) return 0;

        var ids = registrationIds.ToList();
        var regs = await _context.Registrations
            .Where(r => ids.Contains(r.RegistrationId) && r.JobId == jobId)
            .ToListAsync(ct);

        var dayValue = NormalizeBlank(dayGroup);
        var nightValue = NormalizeBlank(nightGroup);

        foreach (var reg in regs)
        {
            if (updateDayGroup) reg.DayGroup = dayValue;
            if (updateNightGroup) reg.NightGroup = nightValue;
        }

        await _context.SaveChangesAsync(ct);
        return regs.Count;
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
