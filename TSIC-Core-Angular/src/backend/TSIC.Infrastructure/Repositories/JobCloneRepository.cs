using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for the SuperUser job-clone feature — loads source data and persists cloned entities.
/// </summary>
public class JobCloneRepository : IJobCloneRepository
{
    private readonly SqlDbContext _context;

    /// <summary>
    /// Admin roles whose registrations are cloned into the new job.
    /// </summary>
    private static readonly string[] AdminRoleIds =
    [
        RoleConstants.Superuser,
        RoleConstants.SuperDirector,
        RoleConstants.Director,
    ];

    public JobCloneRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ══════════════════════════════════════
    // Source data loading (all AsNoTracking)
    // ══════════════════════════════════════

    public async Task<Jobs?> GetSourceJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.JobId == jobId, ct);
    }

    public async Task<JobDisplayOptions?> GetSourceDisplayOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobDisplayOptions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.JobId == jobId, ct);
    }

    public async Task<JobOwlImages?> GetSourceOwlImagesAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobOwlImages
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.JobId == jobId, ct);
    }

    public async Task<List<Bulletins>> GetSourceBulletinsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Bulletins
            .AsNoTracking()
            .Where(b => b.JobId == jobId)
            .ToListAsync(ct);
    }

    public async Task<List<JobAgeRanges>> GetSourceAgeRangesAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobAgeRanges
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .ToListAsync(ct);
    }

    public async Task<List<JobMenus>> GetSourceMenusWithItemsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobMenus
            .AsNoTracking()
            .Include(m => m.JobMenuItems)
            .Where(m => m.JobId == jobId)
            .ToListAsync(ct);
    }

    public async Task<List<Registrations>> GetSourceAdminRegistrationsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.RoleId != null && AdminRoleIds.Contains(r.RoleId))
            .ToListAsync(ct);
    }

    public async Task<Leagues?> GetSourceLeagueAsync(Guid jobId, CancellationToken ct = default)
    {
        // Prefer the primary league when a job has multiple JobLeagues entries.
        return await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .OrderByDescending(jl => jl.BIsPrimary)
            .Select(jl => jl.League)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<JobLeagues?> GetSourceJobLeagueAsync(Guid jobId, Guid leagueId, CancellationToken ct = default)
    {
        // Per-league fee fields live on this link row (BaseFee, LateFee*, DiscountFee*).
        return await _context.JobLeagues
            .AsNoTracking()
            .FirstOrDefaultAsync(jl => jl.JobId == jobId && jl.LeagueId == leagueId, ct);
    }

    public async Task<List<Agegroups>> GetSourceAgegroupsAsync(Guid leagueId, string? season, CancellationToken ct = default)
    {
        var query = _context.Agegroups
            .AsNoTracking()
            .Where(ag => ag.LeagueId == leagueId);

        if (!string.IsNullOrEmpty(season))
            query = query.Where(ag => ag.Season == season);

        return await query.ToListAsync(ct);
    }

    public async Task<List<Divisions>> GetSourceDivisionsAsync(List<Guid> agegroupIds, CancellationToken ct = default)
    {
        return await _context.Divisions
            .AsNoTracking()
            .Where(d => agegroupIds.Contains(d.AgegroupId))
            .ToListAsync(ct);
    }

    public async Task<List<TeamCloneSource>> GetSourceTeamsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Joined to Agegroups so the service can apply the WAITLIST/DROPPED filter against
        // the agegroup name (status tokens are encoded as substrings within the name).
        return await (from t in _context.Teams.AsNoTracking()
                      join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
                      where t.JobId == jobId
                      select new TeamCloneSource
                      {
                          Team = t,
                          AgegroupName = ag.AgegroupName,
                      })
            .ToListAsync(ct);
    }

    // ══════════════════════════════════════
    // Validation
    // ══════════════════════════════════════

    public async Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .AnyAsync(j => j.JobPath == jobPath, ct);
    }

    public async Task<bool> JobNameExistsAsync(string jobName, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .AnyAsync(j => j.JobName == jobName, ct);
    }

    // ══════════════════════════════════════
    // Source picker list
    // ══════════════════════════════════════

    public async Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default)
    {
        // Left-join JobLeagues → Leagues so the wizard can seed leagueNameTarget from
        // the actual source league name. Job → League is via the JobLeagues link table.
        return await _context.Jobs
            .AsNoTracking()
            .OrderByDescending(j => j.Year)
            .ThenBy(j => j.JobName)
            .Select(j => new JobCloneSourceDto
            {
                JobId = j.JobId,
                JobPath = j.JobPath,
                JobName = j.JobName ?? j.JobPath,
                Year = j.Year,
                Season = j.Season,
                DisplayName = j.DisplayName,
                CustomerId = j.CustomerId,
                LeagueName = _context.JobLeagues
                    .Where(jl => jl.JobId == j.JobId)
                    .Select(jl => jl.League.LeagueName)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
    }

    // ══════════════════════════════════════
    // Write operations
    // ══════════════════════════════════════

    public void AddJob(Jobs job)
        => _context.Jobs.Add(job);

    public void AddDisplayOptions(JobDisplayOptions options)
        => _context.JobDisplayOptions.Add(options);

    public void AddOwlImages(JobOwlImages images)
        => _context.JobOwlImages.Add(images);

    public void AddBulletins(IEnumerable<Bulletins> bulletins)
        => _context.Bulletins.AddRange(bulletins);

    public void AddAgeRanges(IEnumerable<JobAgeRanges> ranges)
        => _context.JobAgeRanges.AddRange(ranges);

    public void AddMenu(JobMenus menu)
        => _context.JobMenus.Add(menu);

    public void AddMenuItems(IEnumerable<JobMenuItems> items)
        => _context.JobMenuItems.AddRange(items);

    public void AddRegistrations(IEnumerable<Registrations> registrations)
        => _context.Registrations.AddRange(registrations);

    public void AddLeague(Leagues league)
        => _context.Leagues.Add(league);

    public void AddJobLeague(JobLeagues jobLeague)
        => _context.JobLeagues.Add(jobLeague);

    public void AddAgegroups(IEnumerable<Agegroups> agegroups)
        => _context.Agegroups.AddRange(agegroups);

    public void AddDivisions(IEnumerable<Divisions> divisions)
        => _context.Divisions.AddRange(divisions);

    public void AddTeams(IEnumerable<Teams> teams)
        => _context.Teams.AddRange(teams);

    // ══════════════════════════════════════
    // Transaction + commit
    // ══════════════════════════════════════

    public async Task BeginTransactionAsync(CancellationToken ct = default)
        => await _context.Database.BeginTransactionAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
        => await _context.Database.CommitTransactionAsync(ct);

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
        => await _context.Database.RollbackTransactionAsync(ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);

    // ══════════════════════════════════════
    // Release ops
    // ══════════════════════════════════════

    public async Task<Jobs?> GetJobForUpdateAsync(Guid jobId, CancellationToken ct = default)
    {
        // Tracked (no AsNoTracking) — caller mutates + SaveChanges.
        return await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
    }

    public async Task<List<Registrations>> GetRegistrationsForUpdateAsync(
        Guid jobId, IList<Guid> registrationIds, CancellationToken ct = default)
    {
        if (registrationIds == null || registrationIds.Count == 0)
            return new List<Registrations>();

        // Tracked — we mutate BActive + Modified on each row.
        return await _context.Registrations
            .Where(r => r.JobId == jobId && registrationIds.Contains(r.RegistrationId))
            .ToListAsync(ct);
    }

    public async Task<List<ReleasableAdminDto>> GetReleasableAdminsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await (from r in _context.Registrations.AsNoTracking()
                      join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id into uj
                      from user in uj.DefaultIfEmpty()
                      join role in _context.AspNetRoles.AsNoTracking() on r.RoleId equals role.Id into rj
                      from rrole in rj.DefaultIfEmpty()
                      where r.JobId == jobId
                            && r.RoleId != null
                            && AdminRoleIds.Contains(r.RoleId)
                      orderby user.LastName, user.FirstName
                      select new ReleasableAdminDto
                      {
                          RegistrationId = r.RegistrationId,
                          RoleId = r.RoleId!,
                          RoleName = rrole != null ? rrole.Name : null,
                          UserId = r.UserId,
                          FirstName = user != null ? user.FirstName : null,
                          LastName = user != null ? user.LastName : null,
                          Email = user != null ? user.Email : null,
                          BActive = r.BActive ?? false,
                      })
            .ToListAsync(ct);
    }

    // ══════════════════════════════════════════════════════════
    // Dev-only undo
    // ══════════════════════════════════════════════════════════

    private static readonly Guid[] AdminRoleGuids =
    [
        Guid.Parse(RoleConstants.Superuser),
        Guid.Parse(RoleConstants.Director),
        Guid.Parse(RoleConstants.SuperDirector),
    ];

    public async Task<DevUndoCounts> GetDevUndoCountsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Each count is a separate query. Sequential (not Task.WhenAll) per
        // CLAUDE.md DbContext safety rule.
        var allRegs = await _context.Registrations.AsNoTracking()
            .Where(r => r.JobId == jobId).ToListAsync(ct);
        var adminRegs = allRegs.Count(r =>
            !string.IsNullOrEmpty(r.RoleId)
            && AdminRoleGuids.Any(g => string.Equals(r.RoleId, g.ToString(), StringComparison.OrdinalIgnoreCase)));
        var nonAdminRegs = allRegs.Count - adminRegs;

        // RegistrationAccounting joins via Registrations.RegistrationId.
        var regIds = allRegs.Select(r => r.RegistrationId).ToHashSet();
        var regAccounting = regIds.Count == 0 ? 0
            : await _context.RegistrationAccounting.AsNoTracking()
                .CountAsync(ra => ra.RegistrationId != null && regIds.Contains(ra.RegistrationId.Value), ct);

        var teams = await _context.Teams.AsNoTracking().CountAsync(t => t.JobId == jobId, ct);
        var jobFees = await _context.JobFees.AsNoTracking().CountAsync(f => f.JobId == jobId, ct);
        var feeModifierIds = await _context.JobFees.AsNoTracking()
            .Where(f => f.JobId == jobId).Select(f => f.JobFeeId).ToListAsync(ct);
        var feeMods = feeModifierIds.Count == 0 ? 0
            : await _context.FeeModifiers.AsNoTracking()
                .CountAsync(m => feeModifierIds.Contains(m.JobFeeId), ct);
        var bulletins = await _context.Bulletins.AsNoTracking().CountAsync(b => b.JobId == jobId, ct);

        var leagueIds = await _context.JobLeagues.AsNoTracking()
            .Where(jl => jl.JobId == jobId).Select(jl => jl.LeagueId).ToListAsync(ct);
        var agegroupIds = leagueIds.Count == 0 ? new List<Guid>()
            : await _context.Agegroups.AsNoTracking()
                .Where(a => leagueIds.Contains(a.LeagueId))
                .Select(a => a.AgegroupId).ToListAsync(ct);
        var agegroupCount = agegroupIds.Count;
        var divisionCount = agegroupIds.Count == 0 ? 0
            : await _context.Divisions.AsNoTracking()
                .CountAsync(d => agegroupIds.Contains(d.AgegroupId), ct);

        // Ancillary tables: anything with JobId FK that the clone does NOT write to.
        // If any of these have rows, the job has been used post-clone — block undo.
        var ancillary =
            await _context.CalendarEvents.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.DeviceJobs.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.EmailFailures.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.EmailLogs.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.GameClockParams.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobAdminCharges.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobCalendar.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobCustomers.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobDiscountCodes.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobMessages.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobPushNotificationsToAll.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobSmsbroadcasts.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.JobWidget.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.Jobinvoices.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.MonthlyJobStats.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.Nav.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.PushSubscriptionJobs.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.RegForms.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.Schedule.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.Sliders.AsNoTracking().CountAsync(x => x.JobId == jobId, ct)
            + await _context.Stores.AsNoTracking().CountAsync(x => x.JobId == jobId, ct);

        return new DevUndoCounts
        {
            AdminRegistrations = adminRegs,
            NonAdminRegistrations = nonAdminRegs,
            RegistrationAccounting = regAccounting,
            Teams = teams,
            JobFees = jobFees,
            FeeModifiers = feeMods,
            Bulletins = bulletins,
            Agegroups = agegroupCount,
            Divisions = divisionCount,
            AncillaryRows = ancillary,
        };
    }

    public async Task<bool> IsLeagueExclusivelyOwnedByJobAsync(
        Guid jobId, Guid leagueId, CancellationToken ct = default)
    {
        // True = no OTHER job links this league. Safe to delete the league with the job.
        var otherJobLinks = await _context.JobLeagues.AsNoTracking()
            .CountAsync(jl => jl.LeagueId == leagueId && jl.JobId != jobId, ct);
        return otherJobLinks == 0;
    }

    public async Task<JobLeagues?> GetJobLeagueForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        // Tracked because callers will delete it.
        return await _context.JobLeagues
            .OrderByDescending(jl => jl.BIsPrimary)
            .FirstOrDefaultAsync(jl => jl.JobId == jobId, ct);
    }

    public async Task CascadeDeleteJobAsync(Guid jobId, Guid? clonedLeagueId, CancellationToken ct = default)
    {
        // Delete order: leaves → root, respecting FK direction. Each step is a tracked load,
        // RemoveRange, then SaveChanges to flush before the next step (avoids FK violations
        // where the next delete depends on the prior step having actually run).

        // 1. FeeModifiers (children of JobFees)
        var jobFees = await _context.JobFees.Where(f => f.JobId == jobId).ToListAsync(ct);
        if (jobFees.Count > 0)
        {
            var feeIds = jobFees.Select(f => f.JobFeeId).ToList();
            var modifiers = await _context.FeeModifiers
                .Where(m => feeIds.Contains(m.JobFeeId)).ToListAsync(ct);
            if (modifiers.Count > 0) _context.FeeModifiers.RemoveRange(modifiers);
            _context.JobFees.RemoveRange(jobFees);
        }

        // 2. Teams
        var teams = await _context.Teams.Where(t => t.JobId == jobId).ToListAsync(ct);
        if (teams.Count > 0) _context.Teams.RemoveRange(teams);

        // 3. Divisions (children of Agegroups, which are children of Leagues)
        // 4. Agegroups (children of Leagues)
        if (clonedLeagueId.HasValue)
        {
            var agegroups = await _context.Agegroups
                .Where(a => a.LeagueId == clonedLeagueId.Value).ToListAsync(ct);
            if (agegroups.Count > 0)
            {
                var agIds = agegroups.Select(a => a.AgegroupId).ToList();
                var divisions = await _context.Divisions
                    .Where(d => agIds.Contains(d.AgegroupId)).ToListAsync(ct);
                if (divisions.Count > 0) _context.Divisions.RemoveRange(divisions);
                _context.Agegroups.RemoveRange(agegroups);
            }
        }

        // 5. Registrations (admin-only by predicate; safe to delete all for this job)
        var regs = await _context.Registrations.Where(r => r.JobId == jobId).ToListAsync(ct);
        if (regs.Count > 0) _context.Registrations.RemoveRange(regs);

        // 6. JobLeagues + the cloned Leagues (only if exclusively owned)
        var jobLeagues = await _context.JobLeagues.Where(jl => jl.JobId == jobId).ToListAsync(ct);
        if (jobLeagues.Count > 0) _context.JobLeagues.RemoveRange(jobLeagues);

        // Flush so the JobLeagues delete completes before we attempt to delete Leagues
        // (otherwise the FK from JobLeagues → Leagues blocks the parent delete).
        await _context.SaveChangesAsync(ct);

        if (clonedLeagueId.HasValue)
        {
            var league = await _context.Leagues
                .FirstOrDefaultAsync(l => l.LeagueId == clonedLeagueId.Value, ct);
            if (league != null) _context.Leagues.Remove(league);
        }

        // 7. Bulletins
        var bulletins = await _context.Bulletins.Where(b => b.JobId == jobId).ToListAsync(ct);
        if (bulletins.Count > 0) _context.Bulletins.RemoveRange(bulletins);

        // 8. JobAgeRanges
        var ageRanges = await _context.JobAgeRanges.Where(r => r.JobId == jobId).ToListAsync(ct);
        if (ageRanges.Count > 0) _context.JobAgeRanges.RemoveRange(ageRanges);

        // 9. JobMenus + JobMenuItems
        var menus = await _context.JobMenus.Include(m => m.JobMenuItems)
            .Where(m => m.JobId == jobId).ToListAsync(ct);
        if (menus.Count > 0)
        {
            var allItems = menus.SelectMany(m => m.JobMenuItems).ToList();
            if (allItems.Count > 0) _context.JobMenuItems.RemoveRange(allItems);
            _context.JobMenus.RemoveRange(menus);
        }

        // 10. JobOwlImages
        var owl = await _context.JobOwlImages.FirstOrDefaultAsync(o => o.JobId == jobId, ct);
        if (owl != null) _context.JobOwlImages.Remove(owl);

        // 11. JobDisplayOptions
        var display = await _context.JobDisplayOptions.FirstOrDefaultAsync(d => d.JobId == jobId, ct);
        if (display != null) _context.JobDisplayOptions.Remove(display);

        // 12. Jobs (root)
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (job != null) _context.Jobs.Remove(job);

        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<SuspendedJobDto>> GetSuspendedJobsAsync(
        Guid? customerId, CancellationToken ct = default)
    {
        var query = _context.Jobs.AsNoTracking().Where(j => j.BSuspendPublic);
        if (customerId.HasValue)
            query = query.Where(j => j.CustomerId == customerId.Value);

        // Inactive admin count per job via correlated subquery.
        return await query
            .OrderByDescending(j => j.Modified)
            .Select(j => new SuspendedJobDto
            {
                JobId = j.JobId,
                JobPath = j.JobPath,
                JobName = j.JobName ?? j.JobPath,
                Year = j.Year,
                Season = j.Season,
                DisplayName = j.DisplayName,
                CustomerId = j.CustomerId,
                Modified = j.Modified,
                InactiveAdminCount = _context.Registrations
                    .Count(r => r.JobId == j.JobId
                                && r.RoleId != null
                                && AdminRoleIds.Contains(r.RoleId)
                                && (r.BActive == false || r.BActive == null)),
            })
            .ToListAsync(ct);
    }
}
