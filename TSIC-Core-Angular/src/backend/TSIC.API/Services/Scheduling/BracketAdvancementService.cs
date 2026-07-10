using TSIC.Contracts.Constants;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Score-driven bracket advancement (R2). See <see cref="IBracketAdvancementService"/>.
/// Write-forward: the occupant of the next game's slot lives on Leagues.schedule
/// (T1Id/T2Id); the wiring that says which slot comes from this game lives in
/// brackets.AdvancementFeeds.
/// </summary>
public sealed class BracketAdvancementService : IBracketAdvancementService
{
    private readonly IBracketRepository _bracketRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ILogger<BracketAdvancementService> _logger;

    public BracketAdvancementService(
        IBracketRepository bracketRepo,
        IScheduleRepository scheduleRepo,
        ILogger<BracketAdvancementService> logger)
    {
        _bracketRepo = bracketRepo;
        _scheduleRepo = scheduleRepo;
        _logger = logger;
    }

    // A bracket game takes part in the ladder: both slots carry the same bracket round type.
    // Consolation ("C") games are NOT bracket games — they are seeded standalone placement
    // games that never advance a winner, and they may legitimately end in a tie.
    private static bool IsBracketGame(Schedule g) =>
        g.T1Type == g.T2Type && GameRoundTypes.IsBracket(g.T1Type);

    public void EnsureBracketScoreValid(Schedule game)
    {
        if (IsBracketGame(game)
            && game.T1Score.HasValue && game.T2Score.HasValue
            && game.T1Score.Value == game.T2Score.Value)
        {
            throw new BracketTieRejectedException(
                "A single-elimination bracket game cannot end in a tie — enter a decisive score " +
                "(tournament forfeit convention is typically 1-0 or 3-0).");
        }
    }

    public async Task<int> AdvanceWinnerAsync(int gid, string userId, CancellationToken ct = default)
    {
        var source = await _scheduleRepo.GetGameByIdAsync(gid, ct);
        if (source is null || !IsBracketGame(source)) return 0;
        if (source.T1Id is null || source.T2Id is null) return 0;          // both sides must be present
        if (source.T1Score is null || source.T2Score is null) return 0;    // not yet scored
        if (source.T1Score.Value == source.T2Score.Value) return 0;        // ties never advance

        var t1Won = source.T1Score.Value > source.T2Score.Value;
        var winnerId = t1Won ? source.T1Id : source.T2Id;
        var winnerName = t1Won ? source.T1Name : source.T2Name;
        var loserId = t1Won ? source.T2Id : source.T1Id;
        var loserName = t1Won ? source.T2Name : source.T1Name;

        var feeds = await _bracketRepo.GetFeedsBySourceAsync(gid, ct);
        if (feeds.Count == 0) return 0;

        var now = DateTime.Now;
        var advanced = 0;
        foreach (var feed in feeds)
        {
            var target = await _scheduleRepo.GetGameByIdAsync(feed.TargetGid, ct);
            if (target is null) continue;

            // R3 (cheapest): never overwrite a downstream game that has already been
            // played. On a winner-flipping edit that would corrupt a real result
            // (new team, old score). Such games are corrected by the director by hand
            // (the edit UI warns before editing an already-decided bracket game).
            if (target.T1Score.HasValue || target.T2Score.HasValue) continue;

            var isWinner = string.Equals(feed.SourceResult, "Winner", StringComparison.OrdinalIgnoreCase);
            var teamId = isWinner ? winnerId : loserId;
            var teamName = isWinner ? winnerName : loserName;

            if (feed.TargetSlot == 1) { target.T1Id = teamId; target.T1Name = teamName; }
            else { target.T2Id = teamId; target.T2Name = teamName; }
            target.LebUserId = userId;
            target.Modified = now;
            advanced++;
        }

        if (advanced > 0) await _scheduleRepo.SaveChangesAsync(ct);
        _logger.LogInformation(
            "BracketAdvance: source Gid={Gid} filled {N} downstream slot(s).", gid, advanced);
        return advanced;
    }
}
