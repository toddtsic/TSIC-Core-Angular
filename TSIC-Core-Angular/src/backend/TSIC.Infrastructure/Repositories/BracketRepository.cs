using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Data access for the brackets schema (strategy-driven single-elim now,
/// more formats later as data). Reads placed bracket games from
/// Leagues.schedule and reads/writes the brackets.* metadata layer.
/// </summary>
public class BracketRepository : IBracketRepository
{
    private readonly SqlDbContext _context;

    public BracketRepository(SqlDbContext context) => _context = context;

    // A bracket game (vs a round-robin "T" game) has both slot types equal,
    // non-empty and not "T" — the same predicate the legacy engine used.
    public async Task<List<PlacedBracketGame>> GetPlacedBracketGamesAsync(
        Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                     && s.AgegroupId == agegroupId
                     && s.DivId == divId
                     && s.T1Type != null && s.T2Type != null
                     && s.T1Type == s.T2Type
                     && GameRoundTypes.Bracket.Contains(s.T1Type)
                     && s.T1No != null && s.T2No != null)
            .Select(s => new PlacedBracketGame
            {
                Gid = s.Gid,
                RoundType = s.T1Type!,
                Slot1No = s.T1No!.Value,
                Slot2No = s.T2No!.Value
            })
            .ToListAsync(ct);
    }

    public async Task<Templates?> GetTemplateAsync(
        string strategyCode, int bracketSize, string variant = "Standard", CancellationToken ct = default)
    {
        return await _context.Templates
            .AsNoTracking()
            .Where(t => t.Strategy.Code == strategyCode
                     && t.BracketSize == bracketSize
                     && t.Variant == variant)
            .OrderBy(t => t.TemplateId)
            .FirstOrDefaultAsync(ct);
    }

    // Thin single-strategy shim for callers that only project SE brackets.
    public Task<Templates?> GetSeTemplateAsync(int bracketSize, CancellationToken ct = default) =>
        GetTemplateAsync("SE", bracketSize, ct: ct);

    public async Task<List<TemplateGames>> GetTemplateGamesAsync(int templateId, CancellationToken ct = default) =>
        await _context.TemplateGames
            .AsNoTracking()
            .Where(g => g.TemplateId == templateId)
            .ToListAsync(ct);

    public async Task<List<AdvancementRoutes>> GetTemplateRoutesAsync(int templateId, CancellationToken ct = default) =>
        await _context.AdvancementRoutes
            .AsNoTracking()
            .Where(r => r.SourceTemplateGame.TemplateId == templateId)
            .ToListAsync(ct);

    // Tracked — caller may update its TemplateId.
    public async Task<BracketInstances?> GetInstanceAsync(
        Guid jobId, Guid agegroupId, Guid divId, CancellationToken ct = default) =>
        await _context.BracketInstances
            .FirstOrDefaultAsync(
                b => b.JobId == jobId && b.AgegroupId == agegroupId && b.DivId == divId, ct);

    public void AddInstance(BracketInstances instance) => _context.BracketInstances.Add(instance);

    public async Task<List<BracketStrategyDto>> GetStrategiesAsync(CancellationToken ct = default) =>
        await _context.Strategies
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new BracketStrategyDto
            {
                Code = s.Code,
                Name = s.Name,
                IsActive = s.IsActive
            })
            .ToListAsync(ct);

    public async Task<List<AdvancementFeeds>> GetFeedsBySourceAsync(int sourceGid, CancellationToken ct = default) =>
        await _context.AdvancementFeeds
            .AsNoTracking()
            .Where(f => f.SourceGid == sourceGid)
            .ToListAsync(ct);

    public async Task<List<BracketSeeds>> GetBracketSeedsByGidsAsync(
        IReadOnlyCollection<int> gids, CancellationToken ct = default)
    {
        if (gids.Count == 0) return [];
        return await _context.BracketSeeds
            .AsNoTracking()
            .Where(bs => gids.Contains(bs.Gid))
            .ToListAsync(ct);
    }

    public async Task<List<SeedSlotToResolve>> GetSeedSlotsForJobAsync(
        Guid jobId, CancellationToken ct = default) =>
        await (from sa in _context.SeedAssignments.AsNoTracking()
               join bi in _context.BracketInstances.AsNoTracking()
                   on sa.BracketInstanceId equals bi.BracketInstanceId
               where bi.JobId == jobId && sa.SeedDivId != null
               select new SeedSlotToResolve
               {
                   Gid = sa.Gid,
                   TargetSlot = sa.TargetSlot,
                   SeedDivId = sa.SeedDivId!.Value,
                   SeedRank = sa.SeedRank
               })
            .ToListAsync(ct);

    public async Task<HashSet<Guid>> GetIncompletePoolDivIdsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var divs = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                     && s.DivId != null
                     && s.T1Type == "T"
                     && (s.T1Score == null || s.T2Score == null))
            .Select(s => s.DivId!.Value)
            .Distinct()
            .ToListAsync(ct);
        return new HashSet<Guid>(divs);
    }

    public async Task<List<BracketBackfillTarget>> GetDivisionsWithBracketGamesLackingInstanceAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var candidates = await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId
                     && s.AgegroupId != null && s.DivId != null
                     && s.T1Type != null && s.T1Type == s.T2Type
                     && GameRoundTypes.Bracket.Contains(s.T1Type))
            .Select(s => new { AgegroupId = s.AgegroupId!.Value, DivId = s.DivId!.Value })
            .Distinct()
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        var existing = await _context.BracketInstances
            .AsNoTracking()
            .Where(b => b.JobId == jobId && b.DivId != null)
            .Select(b => new { b.AgegroupId, DivId = b.DivId!.Value })
            .ToListAsync(ct);

        var have = new HashSet<(Guid, Guid)>(existing.Select(e => (e.AgegroupId, e.DivId)));

        return candidates
            .Where(c => !have.Contains((c.AgegroupId, c.DivId)))
            .Select(c => new BracketBackfillTarget { AgegroupId = c.AgegroupId, DivId = c.DivId })
            .ToList();
    }

    public async Task ReplaceFeedsAndSeedsAsync(
        int bracketInstanceId,
        IReadOnlyCollection<AdvancementFeeds> feeds,
        IReadOnlyCollection<SeedAssignments> seeds,
        CancellationToken ct = default)
    {
        var existingFeeds = await _context.AdvancementFeeds
            .Where(f => f.BracketInstanceId == bracketInstanceId)
            .ToListAsync(ct);
        _context.AdvancementFeeds.RemoveRange(existingFeeds);

        var existingSeeds = await _context.SeedAssignments
            .Where(s => s.BracketInstanceId == bracketInstanceId)
            .ToListAsync(ct);
        _context.SeedAssignments.RemoveRange(existingSeeds);

        if (feeds.Count > 0) await _context.AdvancementFeeds.AddRangeAsync(feeds, ct);
        if (seeds.Count > 0) await _context.SeedAssignments.AddRangeAsync(seeds, ct);
    }

    // ── Structural QA reads ──

    public async Task<List<BracketInstanceInfo>> GetInstanceInfosForJobAsync(
        Guid jobId, CancellationToken ct = default) =>
        await _context.BracketInstances
            .AsNoTracking()
            .Where(b => b.JobId == jobId && b.DivId != null)
            .Select(b => new BracketInstanceInfo
            {
                BracketInstanceId = b.BracketInstanceId,
                JobId = b.JobId,
                AgegroupId = b.AgegroupId,
                DivId = b.DivId!.Value,
                AgegroupName = b.Agegroup.AgegroupName ?? "",
                DivName = b.Div!.DivName ?? "",
                TemplateId = b.TemplateId,
                BracketSize = b.Template.BracketSize,
                StrategyCode = b.Template.Strategy.Code
            })
            .ToListAsync(ct);

    public async Task<List<SeedAssignments>> GetSeedAssignmentsByInstanceAsync(
        int bracketInstanceId, CancellationToken ct = default) =>
        await _context.SeedAssignments
            .AsNoTracking()
            .Where(s => s.BracketInstanceId == bracketInstanceId)
            .ToListAsync(ct);

    public async Task<List<AdvancementFeeds>> GetFeedsByInstanceAsync(
        int bracketInstanceId, CancellationToken ct = default) =>
        await _context.AdvancementFeeds
            .AsNoTracking()
            .Where(f => f.BracketInstanceId == bracketInstanceId)
            .ToListAsync(ct);

    public async Task<Dictionary<int, DateTime?>> GetGDatesByGidsAsync(
        IReadOnlyCollection<int> gids, CancellationToken ct = default)
    {
        if (gids.Count == 0) return [];
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => gids.Contains(s.Gid))
            .Select(s => new { s.Gid, s.GDate })
            .ToDictionaryAsync(s => s.Gid, s => s.GDate, ct);
    }

    public async Task<int> GetActiveTeamCountByDivAsync(Guid divId, CancellationToken ct = default) =>
        await _context.Teams
            .AsNoTracking()
            .CountAsync(t => t.DivId == divId && t.Active == true, ct);

    public async Task<Teams?> GetTeamTrackedByDivRankAsync(
        Guid divId, int divRank, CancellationToken ct = default) =>
        await _context.Teams.FirstOrDefaultAsync(
            t => t.DivId == divId && t.DivRank == divRank && t.Active == true, ct);

    public async Task<Dictionary<Guid, TeamSeedIdentity>> GetTeamIdentitiesAsync(
        IReadOnlyCollection<Guid> teamIds, CancellationToken ct = default)
    {
        if (teamIds.Count == 0) return [];
        return await _context.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId))
            .Select(t => new TeamSeedIdentity
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                ClubrepRegistrationid = t.ClubrepRegistrationid
            })
            .ToDictionaryAsync(x => x.TeamId, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _context.SaveChangesAsync(ct);
}
