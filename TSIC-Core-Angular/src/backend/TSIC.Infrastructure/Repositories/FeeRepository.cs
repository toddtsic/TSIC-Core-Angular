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
        // League is the top tier of the Deposit/BalanceDue cascade — resolve the
        // agegroup's league. Job is no longer a fee scope for Player/ClubRep.
        var leagueId = await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.AgegroupId == agegroupId)
            .Select(a => (Guid?)a.LeagueId)
            .FirstOrDefaultAsync(ct);

        // Fetch all candidate rows in one query: team-level, agegroup-level, league-level
        var rows = await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && (
                    // Team level
                    (jf.AgegroupId == agegroupId && jf.TeamId == teamId)
                    // Agegroup level
                    || (jf.AgegroupId == agegroupId && jf.TeamId == null)
                    // League level (top tier — replaces job for base fees)
                    || (jf.LeagueId == leagueId && jf.AgegroupId == null && jf.TeamId == null)
                ))
            .Select(jf => new
            {
                jf.Deposit,
                jf.BalanceDue,
                jf.BFullPaymentRequired,
                // Priority: team=3, agegroup=2, league=1
                Priority = jf.TeamId != null ? 3
                         : jf.AgegroupId != null ? 2
                         : 1
            })
            .OrderByDescending(x => x.Priority)
            .ToListAsync(ct);

        if (rows.Count == 0) return ResolvedFee.NotConfigured;

        // Cascade per field: most specific non-null wins
        decimal? deposit = null;
        decimal? balanceDue = null;
        bool? fullPaymentRequired = null;
        foreach (var row in rows)
        {
            deposit ??= row.Deposit;
            balanceDue ??= row.BalanceDue;
            fullPaymentRequired ??= row.BFullPaymentRequired;
        }

        return new ResolvedFee { FeeConfigured = true, Deposit = deposit, BalanceDue = balanceDue, BFullPaymentRequired = fullPaymentRequired };
    }

    public async Task<ResolvedFee?> GetResolvedFeeForAgegroupAsync(
        Guid jobId, string roleId, Guid agegroupId,
        CancellationToken ct = default)
    {
        // League is the fallback tier — resolve the agegroup's league.
        var leagueId = await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.AgegroupId == agegroupId)
            .Select(a => (Guid?)a.LeagueId)
            .FirstOrDefaultAsync(ct);

        var rows = await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && jf.TeamId == null
                && (jf.AgegroupId == agegroupId
                    || (jf.AgegroupId == null && jf.LeagueId == leagueId)))
            .Select(jf => new
            {
                jf.Deposit,
                jf.BalanceDue,
                jf.BFullPaymentRequired,
                Priority = jf.AgegroupId != null ? 2 : 1
            })
            .OrderByDescending(x => x.Priority)
            .ToListAsync(ct);

        if (rows.Count == 0) return ResolvedFee.NotConfigured;

        decimal? deposit = null;
        decimal? balanceDue = null;
        bool? fullPaymentRequired = null;
        foreach (var row in rows)
        {
            deposit ??= row.Deposit;
            balanceDue ??= row.BalanceDue;
            fullPaymentRequired ??= row.BFullPaymentRequired;
        }

        return new ResolvedFee { FeeConfigured = true, Deposit = deposit, BalanceDue = balanceDue, BFullPaymentRequired = fullPaymentRequired };
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
        // League is the top scope for modifiers — resolve the agegroup's league.
        var leagueId = await _context.Agegroups
            .AsNoTracking()
            .Where(a => a.AgegroupId == agegroupId)
            .Select(a => (Guid?)a.LeagueId)
            .FirstOrDefaultAsync(ct);

        // Pull every active modifier across the cascade, tagged with its scope
        // priority (team=3, agegroup=2, league=1). Job-level modifier rows are NOT
        // a source for early-bird/late-fee — league is the top tier for these.
        var rows = await _context.FeeModifiers
            .AsNoTracking()
            .Where(m =>
                m.JobFee.JobId == jobId
                && m.JobFee.RoleId == roleId
                && (
                    // Team scope
                    (m.JobFee.AgegroupId == agegroupId && m.JobFee.TeamId == teamId)
                    // Agegroup scope
                    || (m.JobFee.AgegroupId == agegroupId && m.JobFee.TeamId == null)
                    // League scope (top tier — replaces job-level for modifiers)
                    || (m.JobFee.LeagueId == leagueId && m.JobFee.AgegroupId == null && m.JobFee.TeamId == null)
                )
                && (m.StartDate == null || m.StartDate <= asOfDate)
                && (m.EndDate == null || m.EndDate >= asOfDate))
            .Select(m => new
            {
                Modifier = m,
                Priority = m.JobFee.TeamId != null ? 3
                         : m.JobFee.AgegroupId != null ? 2
                         : 1
            })
            .ToListAsync(ct);

        // Coalesce per modifier type: the most-specific scope that has an active
        // modifier of a given type wins outright — scopes do NOT sum. Multiple
        // active windows of the same type AT that winning scope still stack.
        return rows
            .GroupBy(x => x.Modifier.ModifierType)
            .SelectMany(g =>
            {
                var topPriority = g.Max(x => x.Priority);
                return g.Where(x => x.Priority == topPriority).Select(x => x.Modifier);
            })
            .ToList();
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
                && jf.TeamId == null
                && jf.LeagueId == null)
            .Select(jf => new { jf.Deposit, jf.BalanceDue })
            .SingleOrDefaultAsync(ct);

        if (row == null) return ResolvedFee.NotConfigured;

        return new ResolvedFee { FeeConfigured = true, Deposit = row.Deposit, BalanceDue = row.BalanceDue };
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
                && m.JobFee.LeagueId == null
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
                && (jf.AgegroupId == agegroupId || (jf.AgegroupId == null && jf.LeagueId == null)))
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

        // Map each agegroup to its league — league is the top tier of the cascade
        // (replaces the old job-level default for Player/ClubRep base fees).
        var agegroupLeagues = await _context.Agegroups
            .AsNoTracking()
            .Where(a => agegroupIds.Contains(a.AgegroupId))
            .Select(a => new { a.AgegroupId, a.LeagueId })
            .ToListAsync(ct);
        var leagueByAgegroup = agegroupLeagues.ToDictionary(x => x.AgegroupId, x => x.LeagueId);
        var leagueIds = agegroupLeagues.Select(x => x.LeagueId).Distinct().ToList();

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
                    // League-level rows for relevant leagues (top tier — replaces job)
                    || (jf.TeamId == null && jf.AgegroupId == null
                        && jf.LeagueId != null && leagueIds.Contains(jf.LeagueId.Value))
                ))
            .Select(jf => new
            {
                jf.AgegroupId,
                jf.TeamId,
                jf.LeagueId,
                jf.Deposit,
                jf.BalanceDue,
                jf.BFullPaymentRequired
            })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, ResolvedFee>(teamIds.Count);
        foreach (var ta in teamAgegroups)
        {
            var teamRow = allRows.FirstOrDefault(r => r.TeamId == ta.TeamId);
            var agRow = allRows.FirstOrDefault(r => r.AgegroupId == ta.AgegroupId && r.TeamId == null);
            var leagueId = leagueByAgegroup.TryGetValue(ta.AgegroupId, out var lg) ? (Guid?)lg : null;
            var leagueRow = leagueId != null
                ? allRows.FirstOrDefault(r => r.LeagueId == leagueId && r.AgegroupId == null && r.TeamId == null)
                : null;

            decimal? deposit = teamRow?.Deposit ?? agRow?.Deposit ?? leagueRow?.Deposit;
            decimal? balanceDue = teamRow?.BalanceDue ?? agRow?.BalanceDue ?? leagueRow?.BalanceDue;
            bool? fullPaymentRequired = teamRow?.BFullPaymentRequired ?? agRow?.BFullPaymentRequired ?? leagueRow?.BFullPaymentRequired;

            result[ta.TeamId] = new ResolvedFee
            {
                FeeConfigured = teamRow != null || agRow != null || leagueRow != null,
                Deposit = deposit,
                BalanceDue = balanceDue,
                BFullPaymentRequired = fullPaymentRequired
            };
        }

        return result;
    }

    public async Task<JobFees?> GetTrackedByScopeAsync(
        Guid jobId, string roleId, Guid? agegroupId, Guid? teamId, Guid? leagueId,
        CancellationToken ct = default)
    {
        return await _context.JobFees
            .Include(jf => jf.FeeModifiers)
            .Where(jf => jf.JobId == jobId
                && jf.RoleId == roleId
                && jf.AgegroupId == agegroupId
                && jf.TeamId == teamId
                && jf.LeagueId == leagueId)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<List<JobFees>> GetJobFeesByLeagueAsync(
        Guid jobId, Guid leagueId, CancellationToken ct = default)
    {
        // League-scoped rows only (AgegroupId/TeamId null) — for the LADT league detail panel.
        return await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.JobId == jobId
                && jf.LeagueId == leagueId
                && jf.AgegroupId == null
                && jf.TeamId == null)
            .Include(jf => jf.FeeModifiers)
            .OrderBy(jf => jf.RoleId)
            .ToListAsync(ct);
    }

    public async Task<JobFees?> GetTrackedByIdAsync(Guid jobFeeId, CancellationToken ct = default)
    {
        return await _context.JobFees
            .Include(jf => jf.FeeModifiers)
            .SingleOrDefaultAsync(jf => jf.JobFeeId == jobFeeId, ct);
    }

    public async Task<List<JobFees>> GetByTeamIdAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.TeamId == teamId)
            .Include(jf => jf.FeeModifiers)
            .ToListAsync(ct);
    }

    public async Task<List<JobFees>> GetByAgegroupScopeAsync(Guid agegroupId, CancellationToken ct = default)
    {
        return await _context.JobFees
            .AsNoTracking()
            .Where(jf => jf.AgegroupId == agegroupId && jf.TeamId == null)
            .Include(jf => jf.FeeModifiers)
            .ToListAsync(ct);
    }

    public void Add(JobFees jobFee) => _context.JobFees.Add(jobFee);
    public void AddModifier(FeeModifiers modifier) => _context.FeeModifiers.Add(modifier);
    public void Remove(JobFees jobFee) => _context.JobFees.Remove(jobFee);
    public void RemoveModifier(FeeModifiers modifier) => _context.FeeModifiers.Remove(modifier);

    public async Task<int> DeleteByAgegroupIdAsync(Guid agegroupId, CancellationToken ct = default)
    {
        // Delete modifiers first (FK_FeeModifiers_JobFees), then the agegroup-scoped fee rows.
        await _context.FeeModifiers
            .Where(m => m.JobFee!.AgegroupId == agegroupId)
            .ExecuteDeleteAsync(ct);
        return await _context.JobFees
            .Where(jf => jf.AgegroupId == agegroupId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> DeleteByTeamIdAsync(Guid teamId, CancellationToken ct = default)
    {
        // Delete modifiers first (FK_FeeModifiers_JobFees), then the team-scoped fee rows.
        await _context.FeeModifiers
            .Where(m => m.JobFee!.TeamId == teamId)
            .ExecuteDeleteAsync(ct);
        return await _context.JobFees
            .Where(jf => jf.TeamId == teamId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default) => await _context.SaveChangesAsync(ct);
}
