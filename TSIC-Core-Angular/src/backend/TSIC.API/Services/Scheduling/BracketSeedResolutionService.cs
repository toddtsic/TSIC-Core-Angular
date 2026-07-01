using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Seed resolution (R1). See <see cref="IBracketSeedResolutionService"/>.
/// Write-forward, mirroring advancement (R2): the occupant of a leaf slot lives
/// on Leagues.schedule (T1Id/T2Id); the (division, rank) that fills it lives in
/// brackets.SeedAssignments; the ranked team comes from pool-play standings.
/// </summary>
public sealed class BracketSeedResolutionService : IBracketSeedResolutionService
{
    private readonly IBracketRepository _bracketRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ILogger<BracketSeedResolutionService> _logger;

    public BracketSeedResolutionService(
        IBracketRepository bracketRepo,
        IScheduleRepository scheduleRepo,
        ILogger<BracketSeedResolutionService> logger)
    {
        _bracketRepo = bracketRepo;
        _scheduleRepo = scheduleRepo;
        _logger = logger;
    }

    public async Task<int> ResolveJobAsync(
        Guid jobId,
        string userId,
        Func<CancellationToken, Task<StandingsByDivisionResponse>> standingsProvider,
        CancellationToken ct = default)
    {
        // Cheap gate: nothing to do for a job with no materialized seed slots.
        var slots = await _bracketRepo.GetSeedSlotsForJobAsync(jobId, ct);
        if (slots.Count == 0) return 0;

        // A pool whose games aren't all scored has no final rank — its seeds wait.
        var incomplete = await _bracketRepo.GetIncompletePoolDivIdsAsync(jobId, ct);

        // Standings are already sorted (incl. tiebreak rules) → 1-based position = rank.
        var standings = await standingsProvider(ct);
        var teamByDivRank = new Dictionary<(Guid DivId, int Rank), (Guid TeamId, string Name)>();
        foreach (var div in standings.Divisions)
        {
            var rank = 0;
            foreach (var t in div.Teams)
            {
                rank++;
                teamByDivRank[(div.DivId, rank)] = (t.TeamId, t.TeamName);
            }
        }

        var now = DateTime.Now;
        var resolved = 0;
        foreach (var slot in slots)
        {
            if (incomplete.Contains(slot.SeedDivId)) continue;                       // pool not final
            if (!teamByDivRank.TryGetValue((slot.SeedDivId, slot.SeedRank), out var team))
                continue;                                                            // rank not present (yet)

            var target = await _scheduleRepo.GetGameByIdAsync(slot.Gid, ct);
            if (target is null) continue;

            // R3 guard: never overwrite a bracket game that has already been played.
            if (target.T1Score.HasValue || target.T2Score.HasValue) continue;

            if (slot.TargetSlot == 1)
            {
                if (target.T1Id == team.TeamId) continue;                            // already correct
                target.T1Id = team.TeamId;
                target.T1Name = team.Name;
            }
            else
            {
                if (target.T2Id == team.TeamId) continue;
                target.T2Id = team.TeamId;
                target.T2Name = team.Name;
            }
            target.LebUserId = userId;
            target.Modified = now;
            resolved++;
        }

        if (resolved > 0) await _scheduleRepo.SaveChangesAsync(ct);
        _logger.LogInformation(
            "BracketSeedResolve: job {JobId} — filled {N} seed slot(s).", jobId, resolved);
        return resolved;
    }
}
