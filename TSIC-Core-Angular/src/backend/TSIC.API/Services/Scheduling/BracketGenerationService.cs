using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;


/// <summary>
/// Projects a division's placed bracket games onto its SE template to produce
/// the brackets.* wiring. See <see cref="IBracketGenerationService"/>.
/// </summary>
public sealed class BracketGenerationService : IBracketGenerationService
{
    private readonly IBracketRepository _bracketRepo;
    private readonly IViewScheduleService _viewSchedule;
    private readonly IBracketSeedResolutionService _resolution;
    private readonly ILogger<BracketGenerationService> _logger;

    // Ladder round type -> number of teams entering that round.
    private static readonly Dictionary<string, int> RoundSize = new()
    {
        ["Z"] = 64, ["Y"] = 32, ["X"] = 16, ["Q"] = 8, ["S"] = 4, ["F"] = 2
    };

    public BracketGenerationService(
        IBracketRepository bracketRepo,
        IViewScheduleService viewSchedule,
        IBracketSeedResolutionService resolution,
        ILogger<BracketGenerationService> logger)
    {
        _bracketRepo = bracketRepo;
        _viewSchedule = viewSchedule;
        _resolution = resolution;
        _logger = logger;
    }

    public async Task<int> BackfillJobAsync(Guid jobId, string userId, CancellationToken ct = default)
    {
        var targets = await _bracketRepo.GetDivisionsWithBracketGamesLackingInstanceAsync(jobId, ct);
        if (targets.Count == 0) return 0;

        var materialized = 0;
        foreach (var t in targets)
        {
            try
            {
                var result = await RecomputeDivisionAsync(jobId, t.AgegroupId, t.DivId, userId, ct);
                if (result is not null) materialized++;
            }
            catch (Exception ex)
            {
                // One odd division must not break the admin's scheduling entry.
                _logger.LogWarning(ex,
                    "BracketBackfill: failed to materialize div {DivId} in job {JobId} — skipped.",
                    t.DivId, jobId);
            }
        }

        _logger.LogInformation(
            "BracketBackfill: job {JobId} — materialized {N}/{Total} division(s).",
            jobId, materialized, targets.Count);

        // A completed tournament won't be re-scored, so resolve seeds now: any pool
        // that is already final drops its ranked teams into the freshly-materialized
        // bracket slots. Cheap no-op when the job has no seed slots.
        await _resolution.ResolveJobAsync(
            jobId, userId,
            c => _viewSchedule.GetStandingsAsync(jobId, new ScheduleFilterRequest(), c), ct);

        return materialized;
    }

    public async Task<BracketGenerationResult?> RecomputeDivisionAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default)
    {
        // 1. Placed bracket games (non round-robin) for this division.
        var placed = await _bracketRepo.GetPlacedBracketGamesAsync(jobId, agegroupId, divId, ct);
        if (placed.Count == 0)
        {
            // No bracket games remain — clear any stale wiring so nothing dangles.
            var existing = await _bracketRepo.GetInstanceAsync(jobId, agegroupId, divId, ct);
            if (existing is not null)
            {
                await _bracketRepo.ReplaceFeedsAndSeedsAsync(existing.BracketInstanceId, [], [], ct);
                await _bracketRepo.SaveChangesAsync(ct);
            }
            _logger.LogInformation(
                "BracketGen: no bracket games placed for div {DivId} — wiring cleared.", divId);
            return null;
        }

        // 2. Bracket size = largest ladder round present (bronze 'B' excluded).
        var feedRounds = placed.Where(p => RoundSize.ContainsKey(p.RoundType)).ToList();
        if (feedRounds.Count == 0)
        {
            _logger.LogWarning(
                "BracketGen: div {DivId} has bracket rows but no ladder rounds (types: {Types}).",
                divId, string.Join(",", placed.Select(p => p.RoundType).Distinct()));
            return null;
        }
        var bracketSize = feedRounds.Max(p => RoundSize[p.RoundType]);

        // 3. SE template for that size + its topology (games + routes).
        var template = await _bracketRepo.GetSeTemplateAsync(bracketSize, ct);
        if (template is null)
        {
            _logger.LogWarning(
                "BracketGen: no SE template of size {Size} — run 02-seed-se-templates.sql.", bracketSize);
            return null;
        }
        var games = await _bracketRepo.GetTemplateGamesAsync(template.TemplateId, ct);
        var routes = await _bracketRepo.GetTemplateRoutesAsync(template.TemplateId, ct);

        // 4. Compute each template game's min-label — the identity that matches a
        //    placed row's min(T1No,T2No). Leaf slot label = its seed position; a
        //    fed slot's label = the min-label of the game feeding it. A game's
        //    label = min of its two slot labels.
        var gamesById = games.ToDictionary(g => g.TemplateGameId);
        var sourceOfTargetSlot = routes.ToDictionary(
            r => (r.TargetTemplateGameId, (int)r.TargetSlot), r => r.SourceTemplateGameId);
        var labelMemo = new Dictionary<int, int>();

        int Label(int templateGameId)
        {
            if (labelMemo.TryGetValue(templateGameId, out var cached)) return cached;
            var g = gamesById[templateGameId];
            var label = Math.Min(SlotLabel(g, 1), SlotLabel(g, 2));
            labelMemo[templateGameId] = label;
            return label;
        }

        int SlotLabel(TemplateGames g, int slot)
        {
            var seed = slot == 1 ? g.Slot1Seed : g.Slot2Seed;
            if (seed.HasValue) return seed.Value;                       // leaf: seed position
            return Label(sourceOfTargetSlot[(g.TemplateGameId, slot)]); // fed: source's label
        }

        foreach (var g in games) Label(g.TemplateGameId); // warm the memo

        // 5. Match placed rows and template games by (RoundType, min-label).
        var placedGidByKey = placed.ToDictionary(p => (p.RoundType, p.MinLabel), p => p.Gid);
        var templateGameByKey = games.ToDictionary(
            g => (g.RoundType, labelMemo[g.TemplateGameId]), g => g);

        // 6. Get-or-create the division's bracket instance.
        var instance = await _bracketRepo.GetInstanceAsync(jobId, agegroupId, divId, ct);
        var now = DateTime.Now;
        if (instance is null)
        {
            instance = new BracketInstances
            {
                JobId = jobId,
                AgegroupId = agegroupId,
                DivId = divId,
                TemplateId = template.TemplateId,
                Modified = now,
                LebUserId = userId
            };
            _bracketRepo.AddInstance(instance);
            await _bracketRepo.SaveChangesAsync(ct); // populate BracketInstanceId
        }
        else if (instance.TemplateId != template.TemplateId)
        {
            instance.TemplateId = template.TemplateId;
            instance.Modified = now;
            instance.LebUserId = userId;
            await _bracketRepo.SaveChangesAsync(ct);
        }

        // 7. Feeds — one per route whose BOTH endpoints are placed.
        //    (Bronze routes only materialize if the optional 'B' game was placed.)
        var feeds = new List<AdvancementFeeds>();
        foreach (var r in routes)
        {
            var srcKey = (gamesById[r.SourceTemplateGameId].RoundType, labelMemo[r.SourceTemplateGameId]);
            var tgtKey = (gamesById[r.TargetTemplateGameId].RoundType, labelMemo[r.TargetTemplateGameId]);
            if (placedGidByKey.TryGetValue(srcKey, out var srcGid) &&
                placedGidByKey.TryGetValue(tgtKey, out var tgtGid))
            {
                feeds.Add(new AdvancementFeeds
                {
                    BracketInstanceId = instance.BracketInstanceId,
                    SourceGid = srcGid,
                    SourceResult = r.SourceResult,
                    TargetGid = tgtGid,
                    TargetSlot = r.TargetSlot,
                    Modified = now,
                    LebUserId = userId
                });
            }
        }

        // 8. Seeds — one per placed leaf slot (a slot NOT fed by a route).
        //    Director-entered seed intent (BracketSeeds, cross-pool aware) is the
        //    source of truth; this is the migration of that intent into the
        //    brackets.* schema. Where the director set nothing, default to the
        //    same division at the slot's placed number.
        var seedIntentByGid = (await _bracketRepo.GetBracketSeedsByGidsAsync(
                placed.Select(p => p.Gid).ToList(), ct))
            .GroupBy(bs => bs.Gid)
            .ToDictionary(g => g.Key, g => g.First());

        var fedSlots = new HashSet<(int, int)>(
            routes.Select(r => (r.TargetTemplateGameId, (int)r.TargetSlot)));
        var seeds = new List<SeedAssignments>();
        foreach (var pg in placed)
        {
            if (!templateGameByKey.TryGetValue((pg.RoundType, pg.MinLabel), out var tg))
            {
                _logger.LogWarning(
                    "BracketGen: placed {Round} game Gid={Gid} label={Label} has no template match.",
                    pg.RoundType, pg.Gid, pg.MinLabel);
                continue;
            }
            seedIntentByGid.TryGetValue(pg.Gid, out var intent);
            for (var slot = 1; slot <= 2; slot++)
            {
                if (fedSlots.Contains((tg.TemplateGameId, slot))) continue; // fed, not seeded
                // Template confirms this is a leaf (seed) slot.
                var isSeedSlot = (slot == 1 ? tg.Slot1Seed : tg.Slot2Seed).HasValue;
                if (!isSeedSlot) continue;

                // Same-division default anchored on the placed row's slot number;
                // overridden by the director's cross-pool intent when present.
                var seedDivId = divId;
                var seedRank = slot == 1 ? pg.Slot1No : pg.Slot2No;
                if (intent is not null)
                {
                    var intentDiv = slot == 1 ? intent.T1SeedDivId : intent.T2SeedDivId;
                    var intentRank = slot == 1 ? intent.T1SeedRank : intent.T2SeedRank;
                    if (intentDiv.HasValue)
                    {
                        seedDivId = intentDiv.Value;
                        if (intentRank.HasValue) seedRank = intentRank.Value;
                    }
                }

                seeds.Add(new SeedAssignments
                {
                    BracketInstanceId = instance.BracketInstanceId,
                    Gid = pg.Gid,
                    TargetSlot = (byte)slot,
                    SeedDivId = seedDivId,
                    SeedRank = seedRank,
                    AcrossPoolRank = null,
                    Modified = now,
                    LebUserId = userId
                });
            }
        }

        // 9. Persist idempotently (replace this instance's feeds + seeds).
        await _bracketRepo.ReplaceFeedsAndSeedsAsync(instance.BracketInstanceId, feeds, seeds, ct);
        await _bracketRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "BracketGen: div {DivId} size {Size} — {Games} games, {Feeds} feeds, {Seeds} seeds.",
            divId, bracketSize, placed.Count, feeds.Count, seeds.Count);

        return new BracketGenerationResult
        {
            BracketInstanceId = instance.BracketInstanceId,
            BracketSize = bracketSize,
            GamesPlaced = placed.Count,
            FeedsWritten = feeds.Count,
            SeedsWritten = seeds.Count
        };
    }
}
