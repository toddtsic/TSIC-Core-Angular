using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Utilities;

namespace TSIC.API.Services.Scheduling;

public class BracketSeedService : IBracketSeedService
{
    private readonly IBracketSeedRepository _repo;
    private readonly IJobRepository _jobRepo;
    private readonly IBracketSeedResolutionService _resolution;
    private readonly IViewScheduleService _viewSchedule;

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

    public BracketSeedService(
        IBracketSeedRepository repo,
        IJobRepository jobRepo,
        IBracketSeedResolutionService resolution,
        IViewScheduleService viewSchedule)
    {
        _repo = repo;
        _jobRepo = jobRepo;
        _resolution = resolution;
        _viewSchedule = viewSchedule;
    }

    public async Task<BracketSeedBoardDto> GetBracketGamesAsync(
        Guid jobId, string userId, CancellationToken ct = default)
    {
        // 1. The universe: every non-RR (bracket) game, with current seed data left-joined.
        var bracketGames = await _repo.GetBracketGamesAsync(jobId, ct);

        // 2. Which of those NEED seeding — derived per slot from the bracket structure
        //    already in hand. A slot is fed when a parent-type game in the same division
        //    carries its number; a game earns a row iff at least one slot is an entry point.
        var flagged = FlagSeedability(bracketGames);
        var needSeeding = flagged.Where(g => g.T1Seedable || g.T2Seedable).ToList();
        var neededGids = needSeeding.Select(g => g.Gid).ToHashSet();

        // 3. Reconcile storage to the needed set: BracketSeeds rows exist exactly for games
        //    that need seeding — stale rows (bracket restructured, game no longer an entry
        //    point) go, missing scaffolds are created EMPTY. Seed values are director
        //    decisions; nothing here guesses them.
        var existing = await _repo.GetAllForJobAsync(jobId, ct);
        var stale = existing.Where(bs => !neededGids.Contains(bs.Gid)).ToList();
        if (stale.Count > 0)
            _repo.RemoveRange(stale);

        var existingGids = existing.Select(bs => bs.Gid).ToHashSet();
        var missingGids = neededGids.Where(gid => !existingGids.Contains(gid)).ToList();
        foreach (var gid in missingGids)
        {
            await _repo.AddAsync(new BracketSeeds
            {
                Gid = gid,
                LebUserId = userId,
                Modified = DateTime.Now
            }, ct);
        }

        if (stale.Count > 0 || missingGids.Count > 0)
            await _repo.SaveChangesAsync(ct);

        // 4. Sort: agegroup → earliest bracket round first → slot number. The fetched DTOs
        //    are already current (new scaffolds carry no seed values) — no re-fetch.
        var games = needSeeding
            .OrderBy(g => g.AgegroupName)
            .ThenByDescending(g => BracketTypeOrder.GetValueOrDefault(g.T1Type, 7))
            .ThenBy(g => g.T1No)
            .ToList();

        var isReseed = await _jobRepo.GetReseedTournamentFlagAsync(jobId, ct);
        return new BracketSeedBoardDto { IsReseed = isReseed, Games = games };
    }

    /// <summary>
    /// Stamp per-slot seedability onto each game, in one pass over the job's bracket games.
    /// Slot N of type P is FED when a game of P's parent type in the same division carries
    /// N on either side — its team advances from that game. Everything else (including all
    /// slots of championship games, whose parent is round-robin) is an entry point from pool
    /// play and must be seeded. Games with no division or an unknown type keep both flags
    /// false and are filtered off the board.
    /// </summary>
    private static List<BracketSeedGameDto> FlagSeedability(List<BracketSeedGameDto> bracketGames)
    {
        var slots = new HashSet<(Guid DivId, string Type, int No)>();
        foreach (var g in bracketGames)
        {
            if (g.DivId is not Guid divId) continue;
            slots.Add((divId, g.T1Type, g.T1No));
            slots.Add((divId, g.T2Type, g.T2No));
        }

        var result = new List<BracketSeedGameDto>(bracketGames.Count);
        foreach (var g in bracketGames)
        {
            if (g.DivId is not Guid divId
                || !ParentTypeMap.TryGetValue(g.T1Type, out var parentType))
            {
                result.Add(g);
                continue;
            }

            result.Add(g with
            {
                T1Seedable = parentType == "T" || !slots.Contains((divId, parentType, g.T1No)),
                T2Seedable = parentType == "T" || !slots.Contains((divId, parentType, g.T2No))
            });
        }
        return result;
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
        seed.Modified = DateTime.Now;

        // Update Schedule.T1Name/T2Name with seed annotations
        var schedule = await _repo.GetScheduleTrackedAsync(request.Gid, ct);
        if (schedule != null)
        {
            if (request.T1SeedDivId != null && request.T1SeedRank != null)
            {
                var divName = await _repo.GetDivisionNameAsync(request.T1SeedDivId.Value, ct);
                schedule.T1Name = BracketSlotLabel.Format(
                    schedule.T1Type, schedule.T1No, divName ?? "", request.T1SeedRank.Value);
            }

            if (request.T2SeedDivId != null && request.T2SeedRank != null)
            {
                var divName = await _repo.GetDivisionNameAsync(request.T2SeedDivId.Value, ct);
                schedule.T2Name = BracketSlotLabel.Format(
                    schedule.T2Type, schedule.T2No, divName ?? "", request.T2SeedRank.Value);
            }
        }

        await _repo.SaveChangesAsync(ct);

        // BracketSeeds we just wrote IS the seed source of truth — seed resolution reads it
        // directly, so there is nothing to project. Resolve immediately: a pool that is
        // already final should fill this slot the moment it is seeded.
        if (schedule?.AgegroupId is Guid && schedule.DivId is Guid)
        {
            await _resolution.ResolveJobAsync(
                schedule.JobId, userId,
                (divIds, c) => _viewSchedule.GetStandingsAsync(
                    schedule.JobId, new ScheduleFilterRequest { DivisionIds = [.. divIds] }, c), ct);
        }

        // Return the updated game with seedability re-stamped — the client swaps this row
        // into its board, so the flags must survive the round trip.
        var allGames = await _repo.GetBracketGamesAsync(schedule!.JobId, ct);
        return FlagSeedability(allGames).First(g => g.Gid == request.Gid);
    }

    public async Task<List<BracketSeedDivisionOptionDto>> GetDivisionsForGameAsync(
        int gid, Guid jobId, CancellationToken ct = default)
    {
        // Reseed jobs draw seeds from any round-robin pool across agegroups; normal jobs
        // stay scoped to the bracket game's own agegroup.
        var isReseed = await _jobRepo.GetReseedTournamentFlagAsync(jobId, ct);
        return isReseed
            ? await _repo.GetSeedSourceDivisionsForJobAsync(jobId, ct)
            : await _repo.GetDivisionsForGameAsync(gid, ct);
    }

    public async Task<int> GetRankCeilingAsync(Guid divId, CancellationToken ct = default)
    {
        return await _repo.GetActiveTeamCountByDivAsync(divId, ct);
    }
}
