using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

public class BracketSeedService : IBracketSeedService
{
    private readonly IBracketSeedRepository _repo;

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

    public BracketSeedService(IBracketSeedRepository repo)
    {
        _repo = repo;
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
            }
        }

        await _repo.SaveChangesAsync(ct);

        // 6. Re-fetch to get clean data after creates/deletes
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
}
