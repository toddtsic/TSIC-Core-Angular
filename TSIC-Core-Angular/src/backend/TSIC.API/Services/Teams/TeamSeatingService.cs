using TSIC.Contracts.Dtos.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Teams;

/// <summary>
/// See <see cref="ITeamSeatingService"/>. Reads committed state and writes explicitly.
/// </summary>
public sealed class TeamSeatingService : ITeamSeatingService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ITeamRenameService _teamRename;
    private readonly ILeagueRepository _leagueRepo;

    public TeamSeatingService(
        ITeamRepository teamRepo,
        IScheduleRepository scheduleRepo,
        ITeamRenameService teamRename,
        ILeagueRepository leagueRepo)
    {
        _teamRepo = teamRepo;
        _scheduleRepo = scheduleRepo;
        _teamRename = teamRename;
        _leagueRepo = leagueRepo;
    }

    public async Task EnsureTeamMayLeavePoolAsync(
        Guid teamId, Guid jobId, string action, CancellationToken ct = default)
    {
        if (!await _teamRepo.IsTeamScheduledAsync(teamId, jobId, ct))
            return;

        throw new InvalidOperationException(
            $"This team is in a schedule, so it cannot be {action}. Its rank is a slot in the "
            + "pairing matrix — emptying it would leave its games with no team. Use Pool Assignment "
            + "to swap it into Dropped Teams against a replacement, which fills the rank and carries "
            + "the deactivation and club rep accounting with it.");
    }

    public async Task<TeamSeatingResultDto> ApplyRankChangeAsync(
        Guid teamId, Guid jobId, int newRank, string? newName, string userId,
        CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");

        if (team.JobId != jobId)
            throw new ArgumentException("Team does not belong to this job.");
        if (!team.DivId.HasValue)
            throw new InvalidOperationException("Team has no division assignment.");

        // Ranks and seats commit together or not at all. Without this the method saves four
        // times, and a failure after the ranks land but before the re-seat leaves precisely the
        // split state this service exists to prevent — ranks moved, board not.
        // Null when a caller (the pool transfer) already owns a transaction: join it, let them commit.
        var tx = await _leagueRepo.BeginTransactionIfNoneAsync(ct);
        await using var _ = tx;

        var divId = team.DivId.Value;
        var oldRank = team.DivRank;
        var rankChanged = oldRank != newRank;
        var nameChanged = newName != null && team.TeamName != newName;

        // A rank edit is a positional swap: the team holding the target rank takes the editing
        // team's old one. That keeps ranks contiguous AND is what makes the two teams trade
        // games — the matrix slots never move, only who sits in them.
        string? swappedWith = null;
        if (rankChanged)
        {
            var swapTeam = await _teamRepo.GetTeamByDivRankAsync(divId, newRank, ct);
            if (swapTeam != null)
            {
                swappedWith = swapTeam.TeamName;
                swapTeam.DivRank = oldRank;
                swapTeam.LebUserId = userId;
                swapTeam.Modified = DateTime.Now;
            }

            team.DivRank = newRank;
        }

        team.LebUserId = userId;
        team.Modified = DateTime.Now;
        await _teamRepo.SaveChangesAsync(ct);

        if (rankChanged)
            await _teamRepo.RenumberDivRanksAsync(divId, ct);

        // Rename BEFORE the re-seat so the seat recompute reads the updated name — the schedule's
        // display string is composed during seating, not copied from the row afterwards.
        if (nameChanged)
            await _teamRename.RenameTeamAsync(teamId, jobId, newName!, userId, ct);

        var reseated = await _scheduleRepo
            .SynchronizeScheduleTeamAssignmentsForDivisionAsync(divId, jobId, userId, ct);

        if (tx is not null) await _leagueRepo.CommitTransactionAsync(ct);

        var finalName = newName ?? team.TeamName ?? "";
        return new TeamSeatingResultDto
        {
            TeamName = finalName,
            SwappedWithTeamName = swappedWith,
            GamesReseated = reseated,
            Message = ComposeMessage(finalName, swappedWith, reseated, rankChanged)
        };
    }

    public async Task<int> RenumberAndReseatAsync(
        Guid divId, Guid jobId, string userId, CancellationToken ct = default)
    {
        var tx = await _leagueRepo.BeginTransactionIfNoneAsync(ct);
        await using var _ = tx;

        await _teamRepo.RenumberDivRanksAsync(divId, ct);
        var reseated = await _scheduleRepo
            .SynchronizeScheduleTeamAssignmentsForDivisionAsync(divId, jobId, userId, ct);

        if (tx is not null) await _leagueRepo.CommitTransactionAsync(ct);
        return reseated;
    }

    public async Task<int> ReseatDivisionsAsync(
        Guid jobId, IReadOnlyCollection<Guid> divIds, string userId, CancellationToken ct = default)
    {
        // Both sides of a swap land together: re-seating one pool and not the other would leave
        // the incoming team playing in its new pool while the outgoing one still holds games in
        // the old. Sequential inside the transaction — these share one scoped DbContext.
        var tx = await _leagueRepo.BeginTransactionIfNoneAsync(ct);
        await using var __ = tx;

        var total = 0;
        foreach (var divId in divIds.Distinct())
            total += await _scheduleRepo
                .SynchronizeScheduleTeamAssignmentsForDivisionAsync(divId, jobId, userId, ct);

        if (tx is not null) await _leagueRepo.CommitTransactionAsync(ct);
        return total;
    }

    /// <summary>
    /// Lead with the trade, not the count. "Rank updated" is what the old silent endpoint may as
    /// well have said, and it is exactly the reading that let a half-finished swap look finished.
    /// </summary>
    private static string ComposeMessage(string teamName, string? swappedWith, int reseated, bool rankChanged)
    {
        if (!rankChanged)
            return reseated > 0
                ? $"{reseated} game(s) updated."
                : "No changes made.";

        if (swappedWith == null)
            return reseated > 0
                ? $"{teamName} moved — {reseated} game(s) re-seated."
                : $"{teamName} moved. No games are scheduled for this division yet.";

        return reseated > 0
            ? $"{teamName} and {swappedWith} swapped schedules — {reseated} game(s) re-seated."
            : $"{teamName} and {swappedWith} swapped ranks. No games are scheduled for this division yet.";
    }
}
