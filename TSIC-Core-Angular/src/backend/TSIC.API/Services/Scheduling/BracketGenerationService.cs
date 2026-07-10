using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;


/// <summary>
/// Projects a division's placed bracket games onto its SE template to materialize the
/// advancement feed graph (which game's winner/loser flows into which slot). Seeds are
/// NOT projected: seed intent is read live from Leagues.BracketSeeds by seed resolution,
/// so it need not be copied here and a seeded game need not belong to a template.
/// See <see cref="IBracketGenerationService"/>.
/// </summary>
public sealed class BracketGenerationService : IBracketGenerationService
{
    private readonly IBracketRepository _bracketRepo;
    private readonly ILogger<BracketGenerationService> _logger;

    public BracketGenerationService(
        IBracketRepository bracketRepo,
        ILogger<BracketGenerationService> logger)
    {
        _bracketRepo = bracketRepo;
        _logger = logger;
    }

    public async Task<int> EnsureJobWiringAsync(Guid jobId, string userId, CancellationToken ct = default)
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
                // This runs on the read chokepoint (every pool-game score) and on dashboard
                // load, so one odd division must not break the caller. But a division that
                // fails to materialize gets no seed slots and no feeds: seeding and
                // advancement are dead for it until this is fixed. That is an error, not a
                // warning.
                _logger.LogError(ex,
                    "BracketWiring: div {DivId} in job {JobId} failed to materialize — its bracket " +
                    "will not seed or advance.",
                    t.DivId, jobId);
            }
        }

        _logger.LogInformation(
            "BracketWiring: job {JobId} — materialized {N}/{Total} division(s).",
            jobId, materialized, targets.Count);

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
                await _bracketRepo.ReplaceFeedsAsync(existing.BracketInstanceId, [], ct);
                await _bracketRepo.SaveChangesAsync(ct);
            }
            _logger.LogInformation(
                "BracketGen: no bracket games placed for div {DivId} — wiring cleared.", divId);
            return null;
        }

        // 2. Bracket size = largest ladder round present (bronze 'B' excluded).
        var feedRounds = placed
            .Where(p => GameRoundTypes.LadderRoundSize.ContainsKey(p.RoundType))
            .ToList();
        if (feedRounds.Count == 0)
        {
            _logger.LogWarning(
                "BracketGen: div {DivId} has bracket rows but no ladder rounds (types: {Types}).",
                divId, string.Join(",", placed.Select(p => p.RoundType).Distinct()));
            return null;
        }
        var bracketSize = feedRounds.Max(p => GameRoundTypes.LadderRoundSize[p.RoundType]);

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
        //    placed row's min(T1No,T2No). Single-sourced with bracket generation
        //    (BracketTemplateTopology) so the two can never drift.
        var gamesById = games.ToDictionary(g => g.TemplateGameId);
        var labelMemo = BracketTemplateTopology.ComputeMinLabels(games, routes);

        // 5. Match placed rows and template games by (RoundType, min-label).
        //    A division carries at most one placed row per template game — but real schedules
        //    break that: a bracket placed twice leaves two 'F' rows on the same 1v2 line. Keep
        //    the earliest-placed row and name the strays. Throwing here is strictly worse: the
        //    caller's per-division catch would swallow it and leave the whole division unwired,
        //    so seeding and advancement would die for it, silently, forever.
        var placedByKey = new Dictionary<(string RoundType, int MinLabel), PlacedBracketGame>();
        foreach (var group in placed.GroupBy(p => (p.RoundType, p.MinLabel)))
        {
            var ordered = group.OrderBy(p => p.Gid).ToList();
            placedByKey[group.Key] = ordered[0];

            if (ordered.Count > 1)
            {
                _logger.LogError(
                    "BracketGen: div {DivId} has {N} placed '{Round}' games on bracket line {Label} " +
                    "(Gids {Gids}). Wiring Gid {Kept}; the others are ignored by seeding and " +
                    "advancement. Delete the duplicate game(s) to clear this.",
                    divId, ordered.Count, group.Key.RoundType, group.Key.MinLabel,
                    string.Join(", ", ordered.Select(p => p.Gid)), ordered[0].Gid);
            }
        }

        var placedGidByKey = placedByKey.ToDictionary(kv => kv.Key, kv => kv.Value.Gid);

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

        // 8. Persist idempotently (replace this instance's feed graph). Seeds are not
        //    written here — seed resolution reads them straight from Leagues.BracketSeeds.
        await _bracketRepo.ReplaceFeedsAsync(instance.BracketInstanceId, feeds, ct);
        await _bracketRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "BracketGen: div {DivId} size {Size} — {Games} games, {Feeds} feeds.",
            divId, bracketSize, placed.Count, feeds.Count);

        return new BracketGenerationResult
        {
            BracketInstanceId = instance.BracketInstanceId,
            BracketSize = bracketSize,
            GamesPlaced = placed.Count,
            FeedsWritten = feeds.Count
        };
    }
}
