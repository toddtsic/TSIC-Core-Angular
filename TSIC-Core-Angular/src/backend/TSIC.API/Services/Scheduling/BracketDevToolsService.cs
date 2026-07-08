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

    public BracketDevToolsService(
        IScheduleRepository scheduleRepo,
        IViewScheduleService viewSchedule,
        ILogger<BracketDevToolsService> logger)
    {
        _scheduleRepo = scheduleRepo;
        _viewSchedule = viewSchedule;
        _logger = logger;
    }

    private static bool IsBracketGame(Schedule g) =>
        !string.IsNullOrEmpty(g.T1Type) && g.T1Type == g.T2Type && g.T1Type != "T";

    private static bool IsPoolGame(Schedule g) => g.T1Type == "T" && g.T2Type == "T";

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

    // Reset each game to "unplayed": clear scores/status, and blank DERIVED bracket
    // occupants (pool teams are fixed; brackets.* wiring is left intact so the next
    // auto-score re-seeds/re-advances). Persists once.
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
                g.GStatusCode = 1; // 1 = scheduled (Leagues.GameStatusCodes)
                changed = true;
            }

            // Blank bracket occupants so the next auto-score visibly re-seeds/advances.
            // Pool games keep their fixed teams.
            if (IsBracketGame(g) && (g.T1Id != null || g.T2Id != null))
            {
                g.T1Id = null;
                g.T2Id = null;
                g.T1Name = null;
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

    public async Task<BracketDevActionResult> AutoScorePoolAgegroupAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default)
    {
        // Agegroup scope: pool games across EVERY division in the agegroup. This is the
        // correct granularity for testing bracket seeding — championship games seed
        // cross-pool from these divisions, so all their pools must complete first.
        var games = await _scheduleRepo.GetAgegroupGamesTrackedAsync(jobId, agegroupId, ct);
        var targets = games
            .Where(g => IsPoolGame(g)
                     && g.T1Id != null && g.T2Id != null
                     && (g.T1Score == null || g.T2Score == null))
            .ToList();

        var scored = await ScoreEachAsync(jobId, userId, targets, ct);
        _logger.LogWarning(
            "DEV bracket auto-score-pool (agegroup) — job {JobId} agegroup {AgegroupId}: {N} game(s) scored.",
            jobId, agegroupId, scored);

        return new BracketDevActionResult
        {
            GamesAffected = scored,
            Message = scored == 0
                ? "No unscored pool games in this age group."
                : $"Auto-scored {scored} pool game(s) {WinScore}–{LoseScore}. Completed pools lock standings → bracket seeds resolve."
        };
    }

    public async Task<BracketDevActionResult> AutoScoreBracketRoundAgegroupAsync(
        Guid jobId, Guid agegroupId, string userId, CancellationToken ct = default)
    {
        var games = await _scheduleRepo.GetAgegroupGamesTrackedAsync(jobId, agegroupId, ct);
        var targets = games
            .Where(g => IsBracketGame(g)
                     && g.T1Id != null && g.T2Id != null
                     && (g.T1Score == null || g.T2Score == null))
            .ToList();

        var scored = await ScoreEachAsync(jobId, userId, targets, ct);
        _logger.LogWarning(
            "DEV bracket auto-score-round (agegroup) — job {JobId} agegroup {AgegroupId}: {N} game(s) scored.",
            jobId, agegroupId, scored);

        return new BracketDevActionResult
        {
            GamesAffected = scored,
            Message = scored == 0
                ? "No bracket games are ready — seed the pools first (auto-score pool)."
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
                GStatusCode = 6 // 6 = final
            }, ct);
            scored++;
        }
        return scored;
    }
}
