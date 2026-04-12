using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class FeeRepository : IFeeRepository
{
    private readonly SqlDbContext _context;

    public FeeRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<ResolvedFee?> GetResolvedFeeAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId,
        CancellationToken ct = default)
    {
        // Fetch all candidate rows in one query: team-level, agegroup-level, job-level
        var rows = await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && (
                    // Team level
                    (jf.AgegroupId == agegroupId && jf.TeamId == teamId)
                    // Agegroup level
                    || (jf.AgegroupId == agegroupId && jf.TeamId == null)
                    // Job level
                    || (jf.AgegroupId == null && jf.TeamId == null)
                ))
            .Select(jf => new
            {
                jf.Deposit,
                jf.BalanceDue,
                // Priority: team=3, agegroup=2, job=1
                Priority = jf.TeamId != null ? 3
                         : jf.AgegroupId != null ? 2
                         : 1
            })
            .OrderByDescending(x => x.Priority)
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        // Cascade per field: most specific non-null wins
        decimal? deposit = null;
        decimal? balanceDue = null;
        foreach (var row in rows)
        {
            deposit ??= row.Deposit;
            balanceDue ??= row.BalanceDue;
        }

        return new ResolvedFee { Deposit = deposit, BalanceDue = balanceDue };
    }

    public async Task<ResolvedFee?> GetResolvedFeeForAgegroupAsync(
        Guid jobId, string roleId, Guid agegroupId,
        CancellationToken ct = default)
    {
        var rows = await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && jf.TeamId == null
                && (jf.AgegroupId == agegroupId || jf.AgegroupId == null))
            .Select(jf => new
            {
                jf.Deposit,
                jf.BalanceDue,
                Priority = jf.AgegroupId != null ? 2 : 1
            })
            .OrderByDescending(x => x.Priority)
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        decimal? deposit = null;
        decimal? balanceDue = null;
        foreach (var row in rows)
        {
            deposit ??= row.Deposit;
            balanceDue ??= row.BalanceDue;
        }

        return new ResolvedFee { Deposit = deposit, BalanceDue = balanceDue };
    }

    public async Task<List<FeeModifiers>> GetActiveModifiersAsync(
        Guid jobFeeId, DateTime asOfDate,
        CancellationToken ct = default)
    {
        return await _context.FeeModifiers
            .AsNoTracking()
            .Where(m => m.JobFeeId == jobFeeId
                && (m.StartDate == null || m.StartDate <= asOfDate)
                && (m.EndDate == null || m.EndDate >= asOfDate))
            .ToListAsync(ct);
    }

    public async Task<List<FeeModifiers>> GetActiveModifiersForCascadeAsync(
        Guid jobId, string roleId, Guid agegroupId, Guid teamId, DateTime asOfDate,
        CancellationToken ct = default)
    {
        // Get all modifiers from all cascade levels in a single query
        return await _context.FeeModifiers
            .AsNoTracking()
            .Where(m =>
                m.JobFee.JobId == jobId
                && m.JobFee.RoleId == roleId
                && (
                    // Team level
                    (m.JobFee.AgegroupId == agegroupId && m.JobFee.TeamId == teamId)
                    // Agegroup level
                    || (m.JobFee.AgegroupId == agegroupId && m.JobFee.TeamId == null)
                    // Job level
                    || (m.JobFee.AgegroupId == null && m.JobFee.TeamId == null)
                )
                && (m.StartDate == null || m.StartDate <= asOfDate)
                && (m.EndDate == null || m.EndDate >= asOfDate))
            .ToListAsync(ct);
    }

    public async Task<ResolvedFee?> GetJobLevelFeeAsync(
        Guid jobId, string roleId,
        CancellationToken ct = default)
    {
        var row = await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && jf.AgegroupId == null
                && jf.TeamId == null)
            .Select(jf => new { jf.Deposit, jf.BalanceDue })
            .SingleOrDefaultAsync(ct);

        if (row == null) return null;

        return new ResolvedFee { Deposit = row.Deposit, BalanceDue = row.BalanceDue };
    }

    public async Task<List<FeeModifiers>> GetActiveModifiersForJobLevelAsync(
        Guid jobId, string roleId, DateTime asOfDate,
        CancellationToken ct = default)
    {
        return await _context.FeeModifiers
            .AsNoTracking()
            .Where(m =>
                m.JobFee.JobId == jobId
                && m.JobFee.RoleId == roleId
                && m.JobFee.AgegroupId == null
                && m.JobFee.TeamId == null
                && (m.StartDate == null || m.StartDate <= asOfDate)
                && (m.EndDate == null || m.EndDate >= asOfDate))
            .ToListAsync(ct);
    }

    public async Task<List<JobFees>> GetJobFeesByJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId)
            .Include(jf => jf.FeeModifiers)
            .OrderBy(jf => jf.RoleId)
            .ThenBy(jf => jf.AgegroupId)
            .ThenBy(jf => jf.TeamId)
            .ToListAsync(ct);
    }

    public async Task<List<JobFees>> GetJobFeesByAgegroupAsync(
        Guid jobId, Guid agegroupId, CancellationToken ct = default)
    {
        return await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && (jf.AgegroupId == agegroupId || jf.AgegroupId == null))
            .Include(jf => jf.FeeModifiers)
            .OrderBy(jf => jf.RoleId)
            .ThenByDescending(jf => jf.AgegroupId)
            .ThenBy(jf => jf.TeamId)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<Guid, ResolvedFee>> GetResolvedFeesByTeamIdsAsync(
        Guid jobId, string roleId, IReadOnlyList<Guid> teamIds,
        CancellationToken ct = default)
    {
        if (teamIds.Count == 0) return new();

        // Get all agegroup IDs for these teams
        var teamAgegroups = await _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId))
            .Select(t => new { t.TeamId, t.AgegroupId })
            .ToListAsync(ct);

        var agegroupIds = teamAgegroups.Select(ta => ta.AgegroupId).Distinct().ToList();

        // Get all potentially relevant fee rows in one query
        var allRows = await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && (
                    // Team-level rows for requested teams
                    (jf.TeamId != null && teamIds.Contains(jf.TeamId.Value))
                    // Agegroup-level rows for relevant agegroups
                    || (jf.TeamId == null && jf.AgegroupId != null && agegroupIds.Contains(jf.AgegroupId.Value))
                    // Job-level default
                    || (jf.TeamId == null && jf.AgegroupId == null)
                ))
            .Select(jf => new
            {
                jf.AgegroupId,
                jf.TeamId,
                jf.Deposit,
                jf.BalanceDue
            })
            .ToListAsync(ct);

        // Job-level default (if any)
        var jobDefault = allRows.FirstOrDefault(r => r.AgegroupId == null && r.TeamId == null);

        var result = new Dictionary<Guid, ResolvedFee>(teamIds.Count);
        foreach (var ta in teamAgegroups)
        {
            var teamRow = allRows.FirstOrDefault(r => r.TeamId == ta.TeamId);
            var agRow = allRows.FirstOrDefault(r => r.AgegroupId == ta.AgegroupId && r.TeamId == null);

            decimal? deposit = teamRow?.Deposit ?? agRow?.Deposit ?? jobDefault?.Deposit;
            decimal? balanceDue = teamRow?.BalanceDue ?? agRow?.BalanceDue ?? jobDefault?.BalanceDue;

            result[ta.TeamId] = new ResolvedFee { Deposit = deposit, BalanceDue = balanceDue };
        }

        return result;
    }

    public async Task<JobFees?> GetTrackedByScopeAsync(
        Guid jobId, string roleId, Guid? agegroupId, Guid? teamId,
        CancellationToken ct = default)
    {
        return await _context.JobFees
            .Include(jf => jf.FeeModifiers)
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && jf.AgegroupId == agegroupId
                && jf.TeamId == teamId)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<JobFees?> GetTrackedByIdAsync(Guid jobFeeId, CancellationToken ct = default)
    {
        return await _context.JobFees
            .Include(jf => jf.FeeModifiers)
            .SingleOrDefaultAsync(jf => jf.JobFeeId == jobFeeId, ct);
    }

    public void Add(JobFees jobFee) => _context.JobFees.Add(jobFee);
    public void AddModifier(FeeModifiers modifier) => _context.FeeModifiers.Add(modifier);
    public void Remove(JobFees jobFee) => _context.JobFees.Remove(jobFee);
    public void RemoveModifier(FeeModifiers modifier) => _context.FeeModifiers.Remove(modifier);
    public async Task SaveChangesAsync(CancellationToken ct = default) => await _context.SaveChangesAsync(ct);
}
