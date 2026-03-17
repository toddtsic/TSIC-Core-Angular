using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class BracketSeedRepository : IBracketSeedRepository
{
    private readonly SqlDbContext _context;

    public BracketSeedRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<BracketSeedGameDto>> GetBracketGamesAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await (
            from s in _context.Schedule
            join ag in _context.Agegroups on s.AgegroupId equals ag.AgegroupId
            join d in _context.Divisions on s.DivId equals d.DivId into divJoin
            from d in divJoin.DefaultIfEmpty()
            join bs in _context.BracketSeeds on s.Gid equals bs.Gid into bsJoin
            from bs in bsJoin.DefaultIfEmpty()
            join t1Div in _context.Divisions on bs.T1SeedDivId equals t1Div.DivId into t1DivJoin
            from t1Div in t1DivJoin.DefaultIfEmpty()
            join t2Div in _context.Divisions on bs.T2SeedDivId equals t2Div.DivId into t2DivJoin
            from t2Div in t2DivJoin.DefaultIfEmpty()
            where s.JobId == jobId
                && s.T1Type != null && s.T2Type != null
                && s.T1Type == s.T2Type
                && s.T1Type != "T"
            select new BracketSeedGameDto
            {
                Gid = s.Gid,
                AgegroupName = (ag.BChampionsByDivision == true)
                    ? $"{ag.AgegroupName}:{d.DivName}"
                    : ag.AgegroupName,
                WhichSide = bs != null ? bs.WhichSide : null,
                T1Type = s.T1Type,
                T1No = s.T1No ?? 0,
                T1SeedDivId = bs != null ? bs.T1SeedDivId : null,
                T1SeedDivName = t1Div != null ? t1Div.DivName : null,
                T1SeedRank = bs != null ? bs.T1SeedRank : null,
                T2Type = s.T2Type,
                T2No = s.T2No ?? 0,
                T2SeedDivId = bs != null ? bs.T2SeedDivId : null,
                T2SeedDivName = t2Div != null ? t2Div.DivName : null,
                T2SeedRank = bs != null ? bs.T2SeedRank : null,
            }
        ).AsNoTracking().ToListAsync(ct);
    }

    public async Task<BracketSeeds?> GetByGidTrackedAsync(
        int gid, CancellationToken ct = default)
    {
        return await _context.BracketSeeds
            .FirstOrDefaultAsync(bs => bs.Gid == gid, ct);
    }

    public async Task<List<BracketSeeds>> GetAllForJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await (
            from bs in _context.BracketSeeds
            join s in _context.Schedule on bs.Gid equals s.Gid
            where s.JobId == jobId
            select bs
        ).ToListAsync(ct);
    }

    public async Task AddAsync(BracketSeeds entity, CancellationToken ct = default)
    {
        await _context.BracketSeeds.AddAsync(entity, ct);
    }

    public void RemoveRange(IEnumerable<BracketSeeds> entities)
    {
        _context.BracketSeeds.RemoveRange(entities);
    }

    public async Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, CancellationToken ct = default)
    {
        var agegroupId = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.Gid == gid)
            .Select(s => s.AgegroupId)
            .FirstOrDefaultAsync(ct);

        if (agegroupId == null || agegroupId == Guid.Empty)
            return [];

        return await _context.Divisions
            .AsNoTracking()
            .Where(d => d.AgegroupId == agegroupId && d.DivName != "Unassigned")
            .OrderBy(d => d.DivName)
            .Select(d => new BracketSeedDivisionOptionDto
            {
                DivId = d.DivId,
                DivName = d.DivName
            })
            .ToListAsync(ct);
    }

    public async Task<Schedule?> GetScheduleTrackedAsync(
        int gid, CancellationToken ct = default)
    {
        return await _context.Schedule.FindAsync([gid], ct);
    }

    public async Task<bool> ParentBracketGameExistsAsync(
        Guid jobId, Guid divId, string parentType, int rank,
        CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s =>
                s.JobId == jobId
                && s.DivId == divId
                && s.T1Type == parentType
                && s.T2Type == parentType
                && (s.T1No == rank || s.T2No == rank))
            .AnyAsync(ct);
    }

    public async Task<string?> GetDivisionNameAsync(
        Guid divId, CancellationToken ct = default)
    {
        return await _context.Divisions
            .AsNoTracking()
            .Where(d => d.DivId == divId)
            .Select(d => d.DivName)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _context.SaveChangesAsync(ct);
    }

    // ── Source job seed lookup (for pre-fill from prior year) ──

    public async Task<List<SourceBracketSeedInfo>> GetSourceBracketSeedsAsync(
        Guid sourceJobId, CancellationToken ct = default)
    {
        return await (
            from bs in _context.BracketSeeds
            join s in _context.Schedule on bs.Gid equals s.Gid
            join ag in _context.Agegroups on s.AgegroupId equals ag.AgegroupId
            join d in _context.Divisions on s.DivId equals d.DivId into divJoin
            from d in divJoin.DefaultIfEmpty()
            join t1Div in _context.Divisions on bs.T1SeedDivId equals t1Div.DivId into t1DivJoin
            from t1Div in t1DivJoin.DefaultIfEmpty()
            join t2Div in _context.Divisions on bs.T2SeedDivId equals t2Div.DivId into t2DivJoin
            from t2Div in t2DivJoin.DefaultIfEmpty()
            where s.JobId == sourceJobId
                && s.T1Type != null && s.T2Type != null
                && s.T1Type == s.T2Type
                && s.T1Type != "T"
            select new SourceBracketSeedInfo
            {
                AgegroupName = (ag.BChampionsByDivision == true)
                    ? $"{ag.AgegroupName}:{d.DivName}"
                    : ag.AgegroupName,
                T1Type = s.T1Type,
                T1No = s.T1No ?? 0,
                T2No = s.T2No ?? 0,
                T1SeedDivName = t1Div != null ? t1Div.DivName : null,
                T1SeedRank = bs.T1SeedRank,
                T2SeedDivName = t2Div != null ? t2Div.DivName : null,
                T2SeedRank = bs.T2SeedRank,
            }
        ).AsNoTracking().ToListAsync(ct);
    }

    public async Task<Dictionary<int, BracketGameContext>> GetBracketGameContextAsync(
        IEnumerable<int> gids, CancellationToken ct = default)
    {
        var gidList = gids.ToList();
        if (gidList.Count == 0)
            return new Dictionary<int, BracketGameContext>();

        return await (
            from s in _context.Schedule
            join ag in _context.Agegroups on s.AgegroupId equals ag.AgegroupId
            join d in _context.Divisions on s.DivId equals d.DivId into divJoin
            from d in divJoin.DefaultIfEmpty()
            where gidList.Contains(s.Gid)
            select new BracketGameContext
            {
                Gid = s.Gid,
                AgegroupName = (ag.BChampionsByDivision == true)
                    ? $"{ag.AgegroupName}:{d.DivName}"
                    : ag.AgegroupName,
                T1Type = s.T1Type ?? "",
                T1No = s.T1No ?? 0,
                T2No = s.T2No ?? 0,
            }
        ).AsNoTracking().ToDictionaryAsync(x => x.Gid, ct);
    }
}
