using Microsoft.EntityFrameworkCore;
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
                     && s.T1Type != "T"
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

    public async Task<Templates?> GetSeTemplateAsync(int bracketSize, CancellationToken ct = default)
    {
        return await _context.Templates
            .AsNoTracking()
            .Where(t => t.Strategy.Code == "SE" && t.BracketSize == bracketSize)
            .OrderBy(t => t.TemplateId)
            .FirstOrDefaultAsync(ct);
    }

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

    public async Task<List<AdvancementFeeds>> GetFeedsBySourceAsync(int sourceGid, CancellationToken ct = default) =>
        await _context.AdvancementFeeds
            .AsNoTracking()
            .Where(f => f.SourceGid == sourceGid)
            .ToListAsync(ct);

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

    public async Task SaveChangesAsync(CancellationToken ct = default) =>
        await _context.SaveChangesAsync(ct);
}
