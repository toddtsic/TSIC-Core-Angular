using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

public class BracketSeedService : IBracketSeedService
{
    private readonly IBracketSeedRepository _repo;
    private readonly IJobRepository _jobRepo;

    /// <summary>
    /// Maps bracket game type to sort order (descending = earliest rounds first in UI).
    /// Z(6)→Y(5)→X(4)→Q(3)→S(2)→F(1)→C(0)
    /// </summary>
    private static readonly Dictionary<string, int> BracketTypeOrder = new()
    {
        ["C"] = 0, ["F"] = 1, ["S"] = 2, ["Q"] = 3,
        ["X"] = 4, ["Y"] = 5, ["Z"] = 6
    };

    /// <summary>
    /// Maps a bracket type to its parent type (the round that feeds into it).
    /// Championship ← RR ("T"), Final ← Semi, Semi ← Quarter, etc.
    /// </summary>
    private static readonly Dictionary<string, string> ParentTypeMap = new()
    {
        ["C"] = "T", ["F"] = "S", ["S"] = "Q", ["Q"] = "X",
        ["X"] = "Y", ["Y"] = "Z"
    };

    public BracketSeedService(IBracketSeedRepository repo, IJobRepository jobRepo)
    {
        _repo = repo;
        _jobRepo = jobRepo;
    }

    public async Task<List<BracketSeedGameDto>> GetBracketGamesAsync(
        Guid jobId, string userId, CancellationToken ct = default)
    {
        // 1. Get all non-RR (bracket) games with current seed data
        var bracketGames = await _repo.GetBracketGamesAsync(jobId, ct);
        var bracketGids = bracketGames.Select(g => g.Gid).ToHashSet();

        // 2. Get existing BracketSeeds records for cleanup
        var existingSeeds = await _repo.GetAllForJobAsync(jobId, ct);

        // 3. Remove orphans: BracketSeeds rows where Gid no longer matches a bracket game
        var orphans = existingSeeds.Where(bs => !bracketGids.Contains(bs.Gid)).ToList();
        if (orphans.Count > 0)
            _repo.RemoveRange(orphans);

        // 4. Determine which games are seedable (leaf bracket games whose parents are RR or don't exist)
        var existingSeedGids = existingSeeds.Select(bs => bs.Gid).ToHashSet();
        var seedableGames = new List<BracketSeedGameDto>();

        foreach (var game in bracketGames)
        {
            if (!ParentTypeMap.TryGetValue(game.T1Type, out var parentType))
                continue;

            var isSeedable = false;

            if (parentType == "T")
            {
                // Championship game — parent is round-robin, always seedable
                isSeedable = true;
            }
            else
            {
                // Check if parent bracket games exist for both T1No and T2No
                // If either parent is missing, this game is seedable (it's a leaf)
                var schedule = await _repo.GetScheduleTrackedAsync(game.Gid, ct);
                if (schedule?.DivId != null)
                {
                    var hasParent1 = await _repo.ParentBracketGameExistsAsync(
                        jobId, schedule.DivId.Value, parentType, game.T1No, ct);
                    var hasParent2 = await _repo.ParentBracketGameExistsAsync(
                        jobId, schedule.DivId.Value, parentType, game.T2No, ct);

                    isSeedable = !hasParent1 || !hasParent2;
                }
            }

            if (isSeedable)
                seedableGames.Add(game);
        }

        // 5. Create missing BracketSeeds records for seedable games
        var newlyCreatedGids = new List<int>();
        foreach (var game in seedableGames)
        {
            if (!existingSeedGids.Contains(game.Gid))
            {
                await _repo.AddAsync(new BracketSeeds
                {
                    Gid = game.Gid,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                }, ct);
                newlyCreatedGids.Add(game.Gid);
            }
        }

        await _repo.SaveChangesAsync(ct);

        // 5.5 Pre-fill seeds from prior year job (auto-discovered)
        if (newlyCreatedGids.Count > 0)
        {
            var priorJob = await _jobRepo.GetPriorYearJobAsync(jobId, ct);
            if (priorJob != null)
            {
                await PreFillSeedsFromSourceAsync(
                    jobId, priorJob.JobId, priorJob.Year, newlyCreatedGids, ct);
            }
        }

        // 6. Re-fetch to get clean data after creates/deletes/pre-fills
        var result = await _repo.GetBracketGamesAsync(jobId, ct);

        // 7. Sort: AgegroupName → bracket type hierarchy descending → T1No
        return result
            .OrderBy(g => g.AgegroupName)
            .ThenByDescending(g => BracketTypeOrder.GetValueOrDefault(g.T1Type, 7))
            .ThenBy(g => g.T1No)
            .ToList();
    }

    public async Task<BracketSeedGameDto> UpdateSeedAsync(
        UpdateBracketSeedRequest request, string userId,
        CancellationToken ct = default)
    {
        var seed = await _repo.GetByGidTrackedAsync(request.Gid, ct)
            ?? throw new InvalidOperationException(
                $"No BracketSeeds record found for game {request.Gid}");

        // Update seed assignments
        seed.T1SeedDivId = request.T1SeedDivId;
        seed.T1SeedRank = request.T1SeedRank;
        seed.T2SeedDivId = request.T2SeedDivId;
        seed.T2SeedRank = request.T2SeedRank;
        seed.LebUserId = userId;
        seed.Modified = DateTime.UtcNow;

        // Update Schedule.T1Name/T2Name with seed annotations
        var schedule = await _repo.GetScheduleTrackedAsync(request.Gid, ct);
        if (schedule != null)
        {
            if (request.T1SeedDivId != null && request.T1SeedRank != null)
            {
                var divName = await _repo.GetDivisionNameAsync(request.T1SeedDivId.Value, ct);
                schedule.T1Name = $"{schedule.T1Type}{schedule.T1No} ({divName}#{request.T1SeedRank})";
            }

            if (request.T2SeedDivId != null && request.T2SeedRank != null)
            {
                var divName = await _repo.GetDivisionNameAsync(request.T2SeedDivId.Value, ct);
                schedule.T2Name = $"{schedule.T2Type}{schedule.T2No} ({divName}#{request.T2SeedRank})";
            }
        }

        await _repo.SaveChangesAsync(ct);

        // Return updated single game DTO
        // Re-fetch the specific game's seed data
        var allGames = await _repo.GetBracketGamesAsync(schedule!.JobId, ct);
        return allGames.First(g => g.Gid == request.Gid);
    }

    public async Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, CancellationToken ct = default)
    {
        return await _repo.GetDivisionsForGameAsync(gid, ct);
    }

    // ── Private: Pre-fill bracket seeds from source/prior year job ──

    /// <summary>
    /// Pre-fill newly created BracketSeeds rows with seed values from the source job,
    /// matching by agegroup name (year-adjusted) + bracket type + slot numbers,
    /// and resolving target division IDs by name matching.
    /// </summary>
    private async Task PreFillSeedsFromSourceAsync(
        Guid jobId, Guid sourceJobId, string sourceYear,
        List<int> newlyCreatedGids, CancellationToken ct)
    {
        // 1. Get source bracket seeds with division names
        var sourceSeeds = await _repo.GetSourceBracketSeedsAsync(sourceJobId, ct);
        if (sourceSeeds.Count == 0) return;

        // 2. Compute year delta for agegroup name mapping
        var targetJobSY = await _jobRepo.GetJobSeasonYearAsync(jobId, ct);
        var targetYear = targetJobSY?.Year;
        var yearDelta = 0;
        if (int.TryParse(targetYear, out var tgt) && int.TryParse(sourceYear, out var src))
            yearDelta = tgt - src;

        // 3. Get target bracket game context (agegroup name, type, slot numbers)
        var targetContext = await _repo.GetBracketGameContextAsync(newlyCreatedGids, ct);
        if (targetContext.Count == 0) return;

        // 4. Build target division name → DivId lookup (per agegroup)
        // Get all division options for a representative game to build the lookup
        var targetAgDivs = new Dictionary<string, Dictionary<string, Guid>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var ctx in targetContext.Values)
        {
            if (targetAgDivs.ContainsKey(ctx.AgegroupName)) continue;
            var divOptions = await _repo.GetDivisionsForGameAsync(ctx.Gid, ct);
            targetAgDivs[ctx.AgegroupName] = divOptions.ToDictionary(
                d => d.DivName, d => d.DivId, StringComparer.OrdinalIgnoreCase);
        }

        // 5. Build source seed lookup: (mappedAgName, T1Type, T1No, T2No) → seed info
        var sourceSeedLookup = new Dictionary<(string AgName, string Type, int T1No, int T2No), SourceBracketSeedInfo>();
        foreach (var ss in sourceSeeds)
        {
            var mappedAgName = yearDelta != 0
                ? AgegroupNameMapper.OffsetName(ss.AgegroupName, yearDelta)
                : ss.AgegroupName;
            var key = (mappedAgName.ToLowerInvariant(), ss.T1Type, ss.T1No, ss.T2No);
            sourceSeedLookup.TryAdd(key, ss);
        }

        // 6. Pre-fill each newly created BracketSeeds row
        var anyUpdated = false;
        foreach (var gid in newlyCreatedGids)
        {
            if (!targetContext.TryGetValue(gid, out var ctx)) continue;

            var lookupKey = (ctx.AgegroupName.ToLowerInvariant(), ctx.T1Type, ctx.T1No, ctx.T2No);
            if (!sourceSeedLookup.TryGetValue(lookupKey, out var sourceSeed)) continue;

            // Resolve target division IDs by name
            Guid? t1DivId = null, t2DivId = null;
            if (sourceSeed.T1SeedDivName != null
                && targetAgDivs.TryGetValue(ctx.AgegroupName, out var agDivLookup))
            {
                agDivLookup.TryGetValue(sourceSeed.T1SeedDivName, out var resolved);
                t1DivId = resolved != Guid.Empty ? resolved : null;
            }
            if (sourceSeed.T2SeedDivName != null
                && targetAgDivs.TryGetValue(ctx.AgegroupName, out agDivLookup))
            {
                agDivLookup.TryGetValue(sourceSeed.T2SeedDivName, out var resolved);
                t2DivId = resolved != Guid.Empty ? resolved : null;
            }

            // Only pre-fill if we resolved at least one side
            if (t1DivId == null && t2DivId == null) continue;

            var seed = await _repo.GetByGidTrackedAsync(gid, ct);
            if (seed == null) continue;

            seed.T1SeedDivId = t1DivId;
            seed.T1SeedRank = t1DivId != null ? sourceSeed.T1SeedRank : null;
            seed.T2SeedDivId = t2DivId;
            seed.T2SeedRank = t2DivId != null ? sourceSeed.T2SeedRank : null;
            seed.Modified = DateTime.UtcNow;
            anyUpdated = true;
        }

        if (anyUpdated)
            await _repo.SaveChangesAsync(ct);
    }
}
