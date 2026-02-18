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
        return await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .Select(jl => jl.League)
            .FirstOrDefaultAsync(ct);
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

    // ══════════════════════════════════════
    // Validation
    // ══════════════════════════════════════

    public async Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .AnyAsync(j => j.JobPath == jobPath, ct);
    }

    // ══════════════════════════════════════
    // Source picker list
    // ══════════════════════════════════════

    public async Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default)
    {
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
}
