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

    public async Task<List<JobReports>> GetSourceJobReportsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobReports
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .ToListAsync(ct);
    }

    public async Task<List<Nav>> GetSourceNavWithItemsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Per-job overrides only — default nav (JobId IS NULL) is shared and not cloned.
        return await _context.Nav
            .AsNoTracking()
            .Include(n => n.NavItem)
            .Where(n => n.JobId == jobId)
            .ToListAsync(ct);
    }

    public async Task<List<Registrations>> GetSourceAdminRegistrationsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.RoleId != null && AdminRoleIds.Contains(r.RoleId))
            .ToListAsync(ct);
    }

    public async Task<List<JobLeagues>> GetSourceJobLeaguesAsync(Guid jobId, CancellationToken ct = default)
    {
        // ALL leagues (T3) — primary first for stable display order.
        return await _context.JobLeagues
            .AsNoTracking()
            .Include(jl => jl.League)
            .Where(jl => jl.JobId == jobId)
            .OrderByDescending(jl => jl.BIsPrimary)
            .ThenBy(jl => jl.League.LeagueName)
            .ToListAsync(ct);
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
        // Joins the classification signals: agegroup name (WAITLIST/Dropped tokens live
        // in it) and the owning club rep's ClubName (empty = director-created house team
        // = structure; real club name = competing team).
        return await (from t in _context.Teams.AsNoTracking()
                      join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
                      join reg in _context.Registrations.AsNoTracking()
                          on t.ClubrepRegistrationid equals reg.RegistrationId into ownerJoin
                      from owner in ownerJoin.DefaultIfEmpty()
                      where t.JobId == jobId
                      select new TeamCloneSource
                      {
                          Team = t,
                          AgegroupName = ag.AgegroupName,
                          OwnerClubName = owner != null ? owner.ClubName : null,
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
        // Group-join (not a correlated per-row subquery): primary league name via a
        // single left join, newest years first for the SU picker.
        return await (from j in _context.Jobs.AsNoTracking()
                      join jl in _context.JobLeagues.AsNoTracking().Where(x => x.BIsPrimary)
                          on j.JobId equals jl.JobId into leagueJoin
                      from jl in leagueJoin.DefaultIfEmpty()
                      orderby j.Year descending, j.JobName
                      select new JobCloneSourceDto
                      {
                          JobId = j.JobId,
                          JobPath = j.JobPath,
                          JobName = j.JobName ?? j.JobPath,
                          Year = j.Year,
                          Season = j.Season,
                          DisplayName = j.DisplayName,
                          CustomerId = j.CustomerId,
                          ExpiryAdmin = j.ExpiryAdmin,
                          ExpiryUsers = j.ExpiryUsers,
                          EventStartDate = j.EventStartDate,
                          EventEndDate = j.EventEndDate,
                          PaymentMethodsAllowedCode = j.PaymentMethodsAllowedCode,
                          LeagueName = jl != null ? jl.League.LeagueName : null,
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

    public void AddJobReports(IEnumerable<JobReports> reports)
        => _context.JobReports.AddRange(reports);

    public void AddNav(Nav nav)
        => _context.Nav.Add(nav);

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

    public async Task<string?> GetCustomerNameAsync(Guid customerId, CancellationToken ct = default)
    {
        return await _context.Customers.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => c.CustomerName)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> CustomerHasAdnCredentialsAsync(Guid customerId, CancellationToken ct = default)
    {
        // Projects to a boolean inside the query — no credential ever materializes in memory.
        return await _context.Customers.AsNoTracking()
            .AnyAsync(c => c.CustomerId == customerId
                           && c.AdnLoginId != null && c.AdnLoginId != ""
                           && c.AdnTransactionKey != null && c.AdnTransactionKey != "", ct);
    }

    // ══════════════════════════════════════════════════════════
    // Dev-only undo
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Entity CLR types the clone manifest writes to — every OTHER entity with a JobId
    /// property is "ancillary": rows there mean the job has been used post-clone.
    /// The D2 drift test asserts this set stays consistent with JobCloneStepOrder.
    /// </summary>
    public static readonly IReadOnlySet<Type> CloneManifestEntityTypes = new HashSet<Type>
    {
        typeof(Jobs),
        typeof(JobDisplayOptions),
        typeof(JobOwlImages),
        typeof(Bulletins),
        typeof(JobAgeRanges),
        typeof(JobMenus),
        typeof(JobMenuItems),
        typeof(JobReports),
        typeof(Nav),
        typeof(NavItem),
        typeof(Registrations),
        typeof(Leagues),
        typeof(JobLeagues),
        typeof(Agegroups),
        typeof(Divisions),
        typeof(Teams),
        typeof(JobFees),
        typeof(FeeModifiers),
    };

    public async Task<DevUndoCounts> GetDevUndoCountsAsync(Guid jobId, CancellationToken ct = default)
    {
        // Each count is a separate query. Sequential (not Task.WhenAll) per
        // CLAUDE.md DbContext safety rule.
        var allRegs = await _context.Registrations.AsNoTracking()
            .Where(r => r.JobId == jobId).ToListAsync(ct);
        var adminRegs = allRegs.Count(r =>
            !string.IsNullOrEmpty(r.RoleId)
            && AdminRoleIds.Any(a => string.Equals(r.RoleId, a, StringComparison.OrdinalIgnoreCase)));
        var nonAdminRegs = allRegs.Count - adminRegs;

        // RegistrationAccounting joins via Registrations.RegistrationId.
        var regIds = allRegs.Select(r => r.RegistrationId).ToHashSet();
        var regAccounting = regIds.Count == 0 ? 0
            : await _context.RegistrationAccounting.AsNoTracking()
                .CountAsync(ra => ra.RegistrationId != null && regIds.Contains(ra.RegistrationId.Value), ct);

        var teams = await _context.Teams.AsNoTracking().CountAsync(t => t.JobId == jobId, ct);
        var jobFees = await _context.JobFees.AsNoTracking().CountAsync(f => f.JobId == jobId, ct);
        var feeIds = await _context.JobFees.AsNoTracking()
            .Where(f => f.JobId == jobId).Select(f => f.JobFeeId).ToListAsync(ct);
        var feeMods = feeIds.Count == 0 ? 0
            : await _context.FeeModifiers.AsNoTracking()
                .CountAsync(m => feeIds.Contains(m.JobFeeId), ct);
        var bulletins = await _context.Bulletins.AsNoTracking().CountAsync(b => b.JobId == jobId, ct);

        var leagueIds = await _context.JobLeagues.AsNoTracking()
            .Where(jl => jl.JobId == jobId).Select(jl => jl.LeagueId).ToListAsync(ct);
        var agegroupIds = leagueIds.Count == 0 ? new List<Guid>()
            : await _context.Agegroups.AsNoTracking()
                .Where(a => leagueIds.Contains(a.LeagueId))
                .Select(a => a.AgegroupId).ToListAsync(ct);
        var divisionCount = agegroupIds.Count == 0 ? 0
            : await _context.Divisions.AsNoTracking()
                .CountAsync(d => agegroupIds.Contains(d.AgegroupId), ct);

        // ── Ancillary: EF-MODEL WALK (D2) — every mapped table whose entity has a JobId
        //    property and is NOT part of the clone manifest. A new job-scoped table is
        //    counted automatically; rows in any of them block undo.
        //    Raw-SQL counts are relational-only — on the InMemory test provider the walk
        //    is skipped (ancillary=0); real verification runs against SQL Server.
        var ancillary = 0;
        var breakdown = new List<string>();
        if (_context.Database.IsRelational())
        {
            foreach (var (tableSql, columnSql, displayName) in GetAncillaryJobTables())
            {
                // EF1002: the interpolated identifiers come from EF model metadata
                // (bracket-quoted table/column names), never from user input; jobId is a
                // real SQL parameter ({0}).
#pragma warning disable EF1002
                var count = await _context.Database
                    .SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM {tableSql} WHERE {columnSql} = {{0}}", jobId)
                    .SingleAsync(ct);
#pragma warning restore EF1002
                if (count > 0)
                {
                    ancillary += count;
                    breakdown.Add($"{displayName}: {count}");
                }
            }
        }

        return new DevUndoCounts
        {
            AdminRegistrations = adminRegs,
            NonAdminRegistrations = nonAdminRegs,
            RegistrationAccounting = regAccounting,
            Teams = teams,
            JobFees = jobFees,
            FeeModifiers = feeMods,
            Bulletins = bulletins,
            Agegroups = agegroupIds.Count,
            Divisions = divisionCount,
            AncillaryRows = ancillary,
            AncillaryBreakdown = breakdown,
        };
    }

    /// <summary>
    /// The D2 model walk: (quoted table, quoted JobId column, display name) for every
    /// mapped, keyed entity with a Guid JobId property outside the clone manifest.
    /// Public-static-shaped via instance model access; the D2 test walks the same model.
    /// </summary>
    private List<(string TableSql, string ColumnSql, string DisplayName)> GetAncillaryJobTables()
    {
        var result = new List<(string, string, string)>();
        foreach (var entityType in _context.Model.GetEntityTypes())
        {
            if (entityType.FindPrimaryKey() == null) continue;              // keyless views
            if (CloneManifestEntityTypes.Contains(entityType.ClrType)) continue;

            var jobIdProp = entityType.FindProperty("JobId");
            if (jobIdProp == null) continue;
            var clr = Nullable.GetUnderlyingType(jobIdProp.ClrType) ?? jobIdProp.ClrType;
            if (clr != typeof(Guid)) continue;

            var tableName = entityType.GetTableName();
            if (tableName == null) continue;                                // not table-mapped
            var schema = entityType.GetSchema() ?? "dbo";
            var storeObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var columnName = jobIdProp.GetColumnName(storeObject);
            if (columnName == null) continue;

            result.Add(($"[{schema}].[{tableName}]", $"[{columnName}]", tableName));
        }
        return result;
    }

    public async Task<bool> IsLeagueExclusivelyOwnedByJobAsync(
        Guid jobId, Guid leagueId, CancellationToken ct = default)
    {
        // True = no OTHER job links this league. Safe to delete the league with the job.
        var otherJobLinks = await _context.JobLeagues.AsNoTracking()
            .CountAsync(jl => jl.LeagueId == leagueId && jl.JobId != jobId, ct);
        return otherJobLinks == 0;
    }

    public async Task<List<JobLeagues>> GetJobLeaguesForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        // Tracked because callers will delete them.
        return await _context.JobLeagues
            .Where(jl => jl.JobId == jobId)
            .OrderByDescending(jl => jl.BIsPrimary)
            .ToListAsync(ct);
    }

    public async Task CascadeDeleteJobAsync(
        Guid jobId, IReadOnlyList<Guid> clonedLeagueIds, CancellationToken ct = default)
    {
        // MANIFEST-DRIVEN: one delete action per JobCloneStepOrder key, executed in exact
        // REVERSE manifest order. Set-equality with the manifest is checked at runtime on
        // every undo — clone/undo drift fails loudly, never silently.
        var deleteActions = new Dictionary<string, Func<Task>>
        {
            [JobCloneStepOrder.FeeModifiers] = async () =>
            {
                var feeIds = await _context.JobFees
                    .Where(f => f.JobId == jobId).Select(f => f.JobFeeId).ToListAsync(ct);
                if (feeIds.Count > 0)
                {
                    var mods = await _context.FeeModifiers
                        .Where(m => feeIds.Contains(m.JobFeeId)).ToListAsync(ct);
                    if (mods.Count > 0) _context.FeeModifiers.RemoveRange(mods);
                }
            },
            [JobCloneStepOrder.JobFees] = async () =>
            {
                var fees = await _context.JobFees.Where(f => f.JobId == jobId).ToListAsync(ct);
                if (fees.Count > 0) _context.JobFees.RemoveRange(fees);
            },
            [JobCloneStepOrder.Teams] = async () =>
            {
                var teams = await _context.Teams.Where(t => t.JobId == jobId).ToListAsync(ct);
                if (teams.Count > 0) _context.Teams.RemoveRange(teams);
            },
            [JobCloneStepOrder.Divisions] = async () =>
            {
                if (clonedLeagueIds.Count == 0) return;
                var agIds = await _context.Agegroups
                    .Where(a => clonedLeagueIds.Contains(a.LeagueId))
                    .Select(a => a.AgegroupId).ToListAsync(ct);
                if (agIds.Count == 0) return;
                var divs = await _context.Divisions
                    .Where(d => agIds.Contains(d.AgegroupId)).ToListAsync(ct);
                if (divs.Count > 0) _context.Divisions.RemoveRange(divs);
            },
            [JobCloneStepOrder.Agegroups] = async () =>
            {
                if (clonedLeagueIds.Count == 0) return;
                var ags = await _context.Agegroups
                    .Where(a => clonedLeagueIds.Contains(a.LeagueId)).ToListAsync(ct);
                if (ags.Count > 0) _context.Agegroups.RemoveRange(ags);
            },
            [JobCloneStepOrder.JobLeagues] = async () =>
            {
                var links = await _context.JobLeagues.Where(jl => jl.JobId == jobId).ToListAsync(ct);
                if (links.Count > 0) _context.JobLeagues.RemoveRange(links);
                // Flush so the link rows are gone before the Leagues parent delete runs.
                await _context.SaveChangesAsync(ct);
            },
            [JobCloneStepOrder.Leagues] = async () =>
            {
                if (clonedLeagueIds.Count == 0) return;
                var leagues = await _context.Leagues
                    .Where(l => clonedLeagueIds.Contains(l.LeagueId)).ToListAsync(ct);
                if (leagues.Count > 0) _context.Leagues.RemoveRange(leagues);
            },
            [JobCloneStepOrder.AdminRegistrations] = async () =>
            {
                // Admin-only by upstream predicate (non-admin regs block the undo).
                var regs = await _context.Registrations.Where(r => r.JobId == jobId).ToListAsync(ct);
                if (regs.Count > 0) _context.Registrations.RemoveRange(regs);
            },
            [JobCloneStepOrder.Nav] = async () =>
            {
                var navs = await _context.Nav.Include(n => n.NavItem)
                    .Where(n => n.JobId == jobId).ToListAsync(ct);
                if (navs.Count > 0)
                {
                    var items = navs.SelectMany(n => n.NavItem).ToList();
                    if (items.Count > 0) _context.NavItem.RemoveRange(items);
                    _context.Nav.RemoveRange(navs);
                }
            },
            [JobCloneStepOrder.JobReports] = async () =>
            {
                var reports = await _context.JobReports.Where(r => r.JobId == jobId).ToListAsync(ct);
                if (reports.Count > 0) _context.JobReports.RemoveRange(reports);
            },
            [JobCloneStepOrder.Menus] = async () =>
            {
                var menus = await _context.JobMenus.Include(m => m.JobMenuItems)
                    .Where(m => m.JobId == jobId).ToListAsync(ct);
                if (menus.Count > 0)
                {
                    var items = menus.SelectMany(m => m.JobMenuItems).ToList();
                    if (items.Count > 0) _context.JobMenuItems.RemoveRange(items);
                    _context.JobMenus.RemoveRange(menus);
                }
            },
            [JobCloneStepOrder.AgeRanges] = async () =>
            {
                var ranges = await _context.JobAgeRanges.Where(r => r.JobId == jobId).ToListAsync(ct);
                if (ranges.Count > 0) _context.JobAgeRanges.RemoveRange(ranges);
            },
            [JobCloneStepOrder.Bulletins] = async () =>
            {
                var bulletins = await _context.Bulletins.Where(b => b.JobId == jobId).ToListAsync(ct);
                if (bulletins.Count > 0) _context.Bulletins.RemoveRange(bulletins);
            },
            [JobCloneStepOrder.OwlImages] = async () =>
            {
                var owl = await _context.JobOwlImages.FirstOrDefaultAsync(o => o.JobId == jobId, ct);
                if (owl != null) _context.JobOwlImages.Remove(owl);
            },
            [JobCloneStepOrder.DisplayOptions] = async () =>
            {
                var display = await _context.JobDisplayOptions.FirstOrDefaultAsync(d => d.JobId == jobId, ct);
                if (display != null) _context.JobDisplayOptions.Remove(display);
            },
            [JobCloneStepOrder.Job] = async () =>
            {
                var job = await _context.Jobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
                if (job != null) _context.Jobs.Remove(job);
            },
        };

        if (!deleteActions.Keys.ToHashSet().SetEquals(JobCloneStepOrder.Steps))
        {
            var missing = JobCloneStepOrder.Steps.Except(deleteActions.Keys).ToList();
            var extra = deleteActions.Keys.Except(JobCloneStepOrder.Steps).ToList();
            throw new InvalidOperationException(
                $"Dev-undo / manifest drift. Missing delete actions: [{string.Join(", ", missing)}]; "
                + $"actions not in manifest: [{string.Join(", ", extra)}].");
        }

        foreach (var key in JobCloneStepOrder.Steps.Reverse())
            await deleteActions[key]();

        await _context.SaveChangesAsync(ct);
    }
}
