using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Dev/sandbox bracket exercise tools. See <see cref="IBracketDevToolsService"/>.
/// Auto-scoring deliberately routes through <see cref="IViewScheduleService"/> so
/// the same validation → advance (R2/R3) → seed-resolve (R1) chain runs as in prod.
/// </summary>
public sealed class BracketDevToolsService : IBracketDevToolsService
{
    private readonly IScheduleRepository _scheduleRepo;
    private readonly IViewScheduleService _viewSchedule;
    private readonly ILogger<BracketDevToolsService> _logger;

    // Deterministic, decisive score for every auto-scored game — a tie would be
    // rejected on bracket games and would muddy pool standings.
    private const int WinScore = 2;
    private const int LoseScore = 1;

    // Leagues.GameStatusCodes: 1 = scheduled, 6 = final.
    private const int ScheduledStatusCode = 1;
    private const int FinalStatusCode = 6;

    public BracketDevToolsService(
        IScheduleRepository scheduleRepo,
        IViewScheduleService viewSchedule,
        ILogger<BracketDevToolsService> logger)
    {
        _scheduleRepo = scheduleRepo;
        _viewSchedule = viewSchedule;
        _logger = logger;
    }

    // Ladder + bronze only. A consolation ("C") game never advances, so it is not a bracket
    // round to auto-score, and its occupant is not derived from the ladder.
    private static bool IsBracketGame(Schedule g) =>
        g.T1Type == g.T2Type && GameRoundTypes.IsBracket(g.T1Type);

    private static bool IsPoolGame(Schedule g) =>
        g.T1Type == GameRoundTypes.RoundRobin && g.T2Type == GameRoundTypes.RoundRobin;

    public async Task<BracketDevActionResult> ClearDivisionScoresAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default)
    {
        // A division revert must ALSO reset the agegroup's championship games: those
        // seed cross-pool from this division, so leaving them scored would strand
        // teams seeded off the standings we're erasing. Fetch the whole agegroup and
        // reset this division's games plus every bracket game in the agegroup.
        var games = await _scheduleRepo.GetAgegroupGamesTrackedAsync(jobId, agegroupId, ct);
        var scope = games.Where(g => g.DivId == divId || IsBracketGame(g)).ToList();
        var affected = await ResetGamesAsync(scope, userId, ct);
        _logger.LogWarning(
            "DEV revert (division) — job {JobId} div {DivId}: {N} game(s) reset (incl. agegroup brackets).",
            jobId, divId, affected);
        return BuildRevertResult(affected, "division");
    }

    public async Task<BracketDevActionResult> ClearAgegroupScoresAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetAgegroupGamesTrackedAsync(jobId, agegroupId, ct);
        var affected = await ResetGamesAsync(games, userId, ct);
        _logger.LogWarning(
            "DEV revert (agegroup) — job {JobId} agegroup {AgegroupId}: {N} game(s) reset.",
            jobId, agegroupId, affected);
        return BuildRevertResult(affected, "agegroup");
    }

    public async Task<BracketDevActionResult> ClearJobScoresAsync(
        Guid jobId, string userId, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetJobGamesTrackedAsync(jobId, ct);
        var affected = await ResetGamesAsync(games, userId, ct);
        _logger.LogWarning(
            "DEV revert (league) — job {JobId}: {N} game(s) reset.", jobId, affected);
        return BuildRevertResult(affected, "league");
    }

    // Return each game to the state PlaceGameAsync mints, then re-resolve it the way the
    // mint does: a "T" slot is seated from Teams.DivRank and survives untouched; a bracket
    // slot is minted empty, so its occupant is always derived state and always goes. The
    // seed annotation that a bracket slot carries before the pools are played lives in
    // BracketSeeds, and is restamped below. Reseed jobs are not a special case — nothing on
    // the mint path seats a bracket slot for any job.
    private async Task<int> ResetGamesAsync(List<Schedule> games, string userId, CancellationToken ct)
    {
        var now = DateTime.Now;
        var affected = 0;

        foreach (var g in games)
        {
            var changed = false;

            if (g.T1Score.HasValue || g.T2Score.HasValue)
            {
                g.T1Score = null;
                g.T2Score = null;
                changed = true;
            }

            if (g.T1penalties.HasValue || g.T2penalties.HasValue)
            {
                g.T1penalties = null;
                g.T2penalties = null;
                changed = true;
            }

            if (g.GStatusCode != ScheduledStatusCode)
            {
                g.GStatusCode = ScheduledStatusCode;
                changed = true;
            }

            // A bracket slot is minted empty, so its occupant is always derived state and can
            // be rebuilt by seed resolution / advancement — blank it. A "T" slot is seated from
            // Teams.DivRank at mint and survives. A consolation slot is neither: nothing on any
            // code path re-seats it, so blanking it would destroy the assignment for good.
            // Decided per slot, as the mint is.
            if (GameRoundTypes.IsBracket(g.T1Type) && (g.T1Id != null || g.T1Name != null))
            {
                g.T1Id = null;
                g.T1Name = null;
                changed = true;
            }

            if (GameRoundTypes.IsBracket(g.T2Type) && (g.T2Id != null || g.T2Name != null))
            {
                g.T2Id = null;
                g.T2Name = null;
                changed = true;
            }

            if (changed)
            {
                g.LebUserId = userId;
                g.Modified = now;
                affected++;
            }
        }

        if (affected > 0) await _scheduleRepo.SaveChangesAsync(ct);

        // Restore the director's seed intent onto the leaf slots we just emptied. Without
        // this the schedule grid would fall back to "X1" while the seed board still showed
        // the intent — and only a manual re-save per game would ever put the label back.
        var bracketGids = games.Where(IsBracketGame).Select(g => g.Gid).ToList();
        await _scheduleRepo.SynchronizeBracketSeedAnnotationsAsync(bracketGids, ct);

        return affected;
    }

    private static BracketDevActionResult BuildRevertResult(int affected, string scope) => new()
    {
        GamesAffected = affected,
        Message = affected == 0
            ? $"Nothing to clear — {scope} already unplayed."
            : $"Reset {affected} game(s) to unplayed; bracket slots blanked, ready to re-seed."
    };

    public async Task<BracketDevActionResult> AutoScorePoolAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetDivisionGamesTrackedAsync(jobId, agegroupId, divId, ct);
        var targets = games
            .Where(g => IsPoolGame(g)
                     && g.T1Id != null && g.T2Id != null
                     && (g.T1Score == null || g.T2Score == null))
            .ToList();

        var scored = await ScoreEachAsync(jobId, userId, targets, ct);
        _logger.LogWarning(
            "DEV bracket auto-score-pool — job {JobId} div {DivId}: {N} game(s) scored.", jobId, divId, scored);

        return new BracketDevActionResult
        {
            GamesAffected = scored,
            Message = scored == 0
                ? "No unscored pool games in this division."
                : $"Auto-scored {scored} pool game(s) {WinScore}–{LoseScore}. Completed pools lock standings → bracket seeds resolve."
        };
    }

    public async Task<BracketDevActionResult> AutoScoreBracketRoundAsync(
        Guid jobId, Guid agegroupId, Guid divId, string userId, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetDivisionGamesTrackedAsync(jobId, agegroupId, divId, ct);
        var targets = games
            .Where(g => IsBracketGame(g)
                     && g.T1Id != null && g.T2Id != null
                     && (g.T1Score == null || g.T2Score == null))
            .ToList();

        var scored = await ScoreEachAsync(jobId, userId, targets, ct);
        _logger.LogWarning(
            "DEV bracket auto-score-round — job {JobId} div {DivId}: {N} game(s) scored.", jobId, divId, scored);

        return new BracketDevActionResult
        {
            GamesAffected = scored,
            Message = scored == 0
                ? "No bracket games are ready — seed the pools first (auto-score pool)."
                : $"Auto-scored {scored} ready bracket game(s) {WinScore}–{LoseScore}. Winners advanced to the next round."
        };
    }

    public async Task<BracketDevActionResult> AutoScorePoolJobAsync(
        Guid jobId, string userId, CancellationToken ct = default)
    {
        // Job scope: every pool game in the event. Reseeding tournaments keep their pools
        // in a dedicated agegroup; each scored pool game fires job-wide seed resolution, so
        // completing the pools reseeds the (separate) championship agegroups automatically.
        var games = await _scheduleRepo.GetJobGamesTrackedAsync(jobId, ct);
        var targets = games
            .Where(g => IsPoolGame(g)
                     && g.T1Id != null && g.T2Id != null
                     && (g.T1Score == null || g.T2Score == null))
            .ToList();

        var scored = await ScoreEachAsync(jobId, userId, targets, ct);
        _logger.LogWarning(
            "DEV bracket auto-score-pool (job) — job {JobId}: {N} pool game(s) scored.", jobId, scored);

        return new BracketDevActionResult
        {
            GamesAffected = scored,
            Message = scored == 0
                ? "No unscored pool games in this event."
                : $"Auto-scored {scored} pool game(s) {WinScore}–{LoseScore}. Completed pools lock standings → bracket seeds resolve."
        };
    }

    public async Task<BracketDevActionResult> AutoScoreBracketRoundJobAsync(
        Guid jobId, string userId, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetJobGamesTrackedAsync(jobId, ct);
        var targets = games
            .Where(g => IsBracketGame(g)
                     && g.T1Id != null && g.T2Id != null
                     && (g.T1Score == null || g.T2Score == null))
            .ToList();

        var scored = await ScoreEachAsync(jobId, userId, targets, ct);
        _logger.LogWarning(
            "DEV bracket auto-score-round (job) — job {JobId}: {N} bracket game(s) scored.", jobId, scored);

        return new BracketDevActionResult
        {
            GamesAffected = scored,
            Message = scored == 0
                ? "No bracket games are ready — seed the pools first (Seed pool scores)."
                : $"Auto-scored {scored} ready bracket game(s) {WinScore}–{LoseScore}. Winners advanced to the next round."
        };
    }

    // Route each score through the real path so R1/R2/R3 fire exactly as in prod.
    private async Task<int> ScoreEachAsync(
        Guid jobId, string userId, List<Schedule> targets, CancellationToken ct)
    {
        var scored = 0;
        foreach (var g in targets)
        {
            await _viewSchedule.QuickEditScoreAsync(jobId, userId, new EditScoreRequest
            {
                Gid = g.Gid,
                T1Score = WinScore,
                T2Score = LoseScore,
                GStatusCode = FinalStatusCode
            }, ct);
            scored++;
        }
        return scored;
    }
}
