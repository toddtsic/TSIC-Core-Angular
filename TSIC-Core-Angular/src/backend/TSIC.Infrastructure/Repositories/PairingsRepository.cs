using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for PairingsLeagueSeason, Masterpairingtable, and BracketDataSingleElimination.
/// </summary>
public class PairingsRepository : IPairingsRepository
{
    private readonly SqlDbContext _context;

    public PairingsRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ── Read: PairingsLeagueSeason ──

    public async Task<List<PairingsLeagueSeason>> GetPairingsAsync(
        Guid leagueId, string season, int teamCount, CancellationToken ct = default)
    {
        return await _context.PairingsLeagueSeason
            .AsNoTracking()
            .Where(p => p.LeagueId == leagueId && p.Season == season && p.TCnt == teamCount)
            .OrderBy(p => p.GameNumber)
            .ToListAsync(ct);
    }

    public async Task<(int maxGameNumber, int maxRound)> GetMaxGameAndRoundAsync(
        Guid leagueId, string season, int teamCount, CancellationToken ct = default)
    {
        var keys = await _context.PairingsLeagueSeason
            .AsNoTracking()
            .Where(p => p.LeagueId == leagueId && p.Season == season && p.TCnt == teamCount)
            .Select(p => new { p.GameNumber, p.Rnd })
            .ToListAsync(ct);

        if (keys.Count == 0)
            return (0, 0);

        return (keys.Max(k => k.GameNumber), keys.Max(k => k.Rnd));
    }

    public async Task<PairingsLeagueSeason?> GetByIdAsync(int ai, CancellationToken ct = default)
    {
        return await _context.PairingsLeagueSeason.FindAsync([ai], ct);
    }

    // ── Read: Seed data ──

    public async Task<List<Masterpairingtable>> GetMasterPairingsAsync(
        int teamCount, int maxRounds, CancellationToken ct = default)
    {
        return await _context.Masterpairingtable
            .AsNoTracking()
            .Where(m => m.TCnt == teamCount && m.Rnd <= maxRounds)
            .ToListAsync(ct);
    }

    public async Task<List<BracketDataSingleElimination>> GetBracketDataAsync(
        string roundType, CancellationToken ct = default)
    {
        return await _context.BracketDataSingleElimination
            .AsNoTracking()
            .Where(b => b.RoundType == roundType)
            .OrderBy(b => b.T1)
            .ToListAsync(ct);
    }

    // ── Read: Availability ──

    public async Task<HashSet<(int Rnd, int T1, int T2)>> GetScheduledPairingKeysAsync(
        Guid leagueId, string season, int teamCount, CancellationToken ct = default)
    {
        // A pairing is "scheduled" when a Schedule row exists with matching
        // LeagueId, Season, Rnd, and team numbers (T1No/T2No).
        // We look at divisions that have the specified team count.
        var divIdsForTCnt = await _context.Teams
            .AsNoTracking()
            .Where(t => t.LeagueId == leagueId && t.Active == true)
            .GroupBy(t => t.DivId)
            .Where(g => g.Count() == teamCount)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (divIdsForTCnt.Count == 0)
            return [];

        var scheduled = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.LeagueId == leagueId
                && s.Season == season
                && s.DivId != null
                && divIdsForTCnt.Contains(s.DivId.Value)
                && s.Rnd != null
                && s.T1No != null
                && s.T2No != null)
            .Select(s => new { Rnd = (int)s.Rnd!.Value, T1 = s.T1No!.Value, T2 = (int)s.T2No!.Value })
            .ToListAsync(ct);

        return scheduled.Select(s => (s.Rnd, s.T1, s.T2)).ToHashSet();
    }

    // ── Read: Agegroup/Division tree ──

    public async Task<List<Agegroups>> GetAgegroupsWithDivisionsAsync(
        Guid leagueId, string season, CancellationToken ct = default)
    {
        return await _context.Agegroups
            .AsNoTracking()
            .Include(ag => ag.Divisions)
            .Where(ag => ag.LeagueId == leagueId && ag.Season == season)
            .OrderBy(ag => ag.SortAge)
            .ToListAsync(ct);
    }

    public async Task<int> GetDivisionTeamCountAsync(Guid divId, Guid jobId, CancellationToken ct = default)
    {
        return await _context.Teams
            .AsNoTracking()
            .CountAsync(t => t.DivId == divId && t.JobId == jobId && t.Active == true, ct);
    }

    // ── Write ──

    public async Task AddRangeAsync(List<PairingsLeagueSeason> pairings, CancellationToken ct = default)
    {
        await _context.PairingsLeagueSeason.AddRangeAsync(pairings, ct);
    }

    public void Remove(PairingsLeagueSeason pairing)
    {
        _context.PairingsLeagueSeason.Remove(pairing);
    }

    public async Task DeleteAllAsync(Guid leagueId, string season, int teamCount, CancellationToken ct = default)
    {
        var toDelete = await _context.PairingsLeagueSeason
            .Where(p => p.LeagueId == leagueId && p.Season == season && p.TCnt == teamCount)
            .ToListAsync(ct);

        _context.PairingsLeagueSeason.RemoveRange(toDelete);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
