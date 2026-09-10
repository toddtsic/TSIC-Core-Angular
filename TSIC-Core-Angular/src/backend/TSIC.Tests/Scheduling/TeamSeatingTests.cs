using FluentAssertions;
using TSIC.API.Services.Teams;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;
using Xunit;

namespace TSIC.Tests.Scheduling;

/// <summary>
/// A pool rank IS the team's slot in the pairing matrix. Changing a rank therefore trades two
/// teams' games — the schedulers' standard way to move a team up or down a strength tier.
///
/// The bug these tests pin: the rank changed and the board did not. Ranks traded, games kept
/// their previous occupants, and nothing said so. It survived because the only thing that
/// reported it was a QA screen nobody had reason to open.
/// </summary>
public class TeamSeatingTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid DivId = Guid.NewGuid();
    private static readonly Guid OtherDivId = Guid.NewGuid();
    private const string UserId = "test-admin";

    /// <summary>
    /// Four teams at ranks 1-4 and the six games of a four-team round robin, seated to match.
    /// Slot numbers never move; only who sits in them.
    /// </summary>
    private static async Task<(SqlDbContext ctx, Dictionary<int, Guid> teamsByRank)> Seed(Guid divId)
    {
        var ctx = DbContextFactory.Create();
        var byRank = new Dictionary<int, Guid>();

        for (var rank = 1; rank <= 4; rank++)
        {
            var teamId = Guid.NewGuid();
            byRank[rank] = teamId;
            ctx.Teams.Add(new Teams
            {
                TeamId = teamId,
                JobId = JobId,
                DivId = divId,
                DivRank = rank,
                TeamName = $"Team {rank}",
                Active = true
            });
        }

        // The six pairings of a four-team round robin.
        var pairings = new[] { (2, 1), (4, 3), (1, 3), (2, 4), (4, 1), (3, 2) };
        var gid = 1;
        foreach (var (t1, t2) in pairings)
        {
            ctx.Schedule.Add(new Schedule
            {
                Gid = gid++,
                JobId = JobId,
                DivId = divId,
                GDate = new DateTime(2026, 11, 22, 12, 0, 0),
                Season = "Fall",
                Year = "2026",
                LebUserId = UserId,
                T1Type = "T",
                T1No = t1,
                T1Id = byRank[t1],
                T1Name = $"Team {t1}",
                T2Type = "T",
                T2No = (byte)t2,
                T2Id = byRank[t2],
                T2Name = $"Team {t2}"
            });
        }

        await ctx.SaveChangesAsync();
        return (ctx, byRank);
    }

    private static TeamSeatingService Service(SqlDbContext ctx)
    {
        var teamRepo = new TeamRepository(ctx);
        var scheduleRepo = new ScheduleRepository(ctx);
        var rename = new TeamRenameService(teamRepo, new ClubTeamRepository(ctx), scheduleRepo);
        return new TeamSeatingService(teamRepo, scheduleRepo, rename, new LeagueRepository(ctx));
    }

    [Fact(DisplayName = "Rank change re-seats the games: the two teams trade schedules")]
    public async Task RankChange_TradesTheTwoTeamsGames()
    {
        var (ctx, byRank) = await Seed(DivId);
        var team2 = byRank[2];
        var team3 = byRank[3];

        // Move rank-2 down to 3 — the scheduler's "these two swap" gesture.
        await Service(ctx).ApplyRankChangeAsync(team2, JobId, newRank: 3, newName: null, UserId);

        // Ranks traded.
        ctx.Teams.Single(t => t.TeamId == team2).DivRank.Should().Be(3);
        ctx.Teams.Single(t => t.TeamId == team3).DivRank.Should().Be(2);

        // And the BOARD followed: every slot-2 seat is now the team that holds rank 2.
        foreach (var g in ctx.Schedule.Where(s => s.DivId == DivId))
        {
            if (g.T1No == 2) g.T1Id.Should().Be(team3, "slot 2 seats whoever holds rank 2");
            if (g.T1No == 3) g.T1Id.Should().Be(team2, "slot 3 seats whoever holds rank 3");
            if (g.T2No == 2) g.T2Id.Should().Be(team3);
            if (g.T2No == 3) g.T2Id.Should().Be(team2);
        }
    }

    [Fact(DisplayName = "Only the two teams involved move — everyone else keeps their games")]
    public async Task RankChange_LeavesTheOtherTeamsAlone()
    {
        var (ctx, byRank) = await Seed(DivId);
        var team1 = byRank[1];
        var team4 = byRank[4];

        var before = ctx.Schedule
            .Where(s => s.T1Id == team1 || s.T2Id == team1 || s.T1Id == team4 || s.T2Id == team4)
            .Select(s => new { s.Gid, s.T1Id, s.T2Id })
            .OrderBy(s => s.Gid).ToList();

        await Service(ctx).ApplyRankChangeAsync(byRank[2], JobId, newRank: 3, newName: null, UserId);

        var after = ctx.Schedule
            .Where(s => before.Select(b => b.Gid).Contains(s.Gid))
            .Select(s => new { s.Gid, s.T1Id, s.T2Id })
            .OrderBy(s => s.Gid).ToList();

        // Teams 1 and 4 never changed rank, so their seats must be untouched — the games they
        // are in still hold them, in the same slots.
        after.Where(a => a.T1Id == team1 || a.T2Id == team1 || a.T1Id == team4 || a.T2Id == team4)
             .Should().HaveCount(before.Count);
        ctx.Teams.Single(t => t.TeamId == team1).DivRank.Should().Be(1);
        ctx.Teams.Single(t => t.TeamId == team4).DivRank.Should().Be(4);
    }

    [Fact(DisplayName = "The result names both teams and counts the games, so the screen can say so")]
    public async Task RankChange_ReportsTheTrade()
    {
        var (ctx, byRank) = await Seed(DivId);

        var result = await Service(ctx)
            .ApplyRankChangeAsync(byRank[2], JobId, newRank: 3, newName: null, UserId);

        result.TeamName.Should().Be("Team 2");
        result.SwappedWithTeamName.Should().Be("Team 3");
        // Five of the six games hold rank 2 or rank 3 — the sixth is rank 4 v rank 1 and is
        // untouched. Six SEATS move, because one of the five is the two teams' head-to-head;
        // the count the scheduler reads is games, so it is 5.
        result.GamesReseated.Should().Be(5);
        result.Message.Should().Contain("swapped schedules");
    }

    [Fact(DisplayName = "A pool with no schedule yet reports zero re-seated rather than claiming games moved")]
    public async Task RankChange_WithNoSchedule_ReportsNothingReseated()
    {
        var (ctx, byRank) = await Seed(DivId);
        ctx.Schedule.RemoveRange(ctx.Schedule);
        await ctx.SaveChangesAsync();

        var result = await Service(ctx)
            .ApplyRankChangeAsync(byRank[2], JobId, newRank: 3, newName: null, UserId);

        result.GamesReseated.Should().Be(0);
        result.Message.Should().Contain("No games are scheduled");
    }

    [Fact(DisplayName = "A scheduled team cannot be deleted, dropped or inactivated")]
    public async Task ScheduledTeam_CannotLeaveItsPool()
    {
        var (ctx, byRank) = await Seed(DivId);
        var svc = Service(ctx);

        foreach (var verb in new[] { "deleted", "dropped", "inactivated" })
        {
            var act = async () => await svc.EnsureTeamMayLeavePoolAsync(byRank[2], JobId, verb);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage($"*cannot be {verb}*");
        }
    }

    [Fact(DisplayName = "An unscheduled team may leave its pool")]
    public async Task UnscheduledTeam_MayLeaveItsPool()
    {
        var (ctx, _) = await Seed(DivId);
        var spare = Guid.NewGuid();
        ctx.Teams.Add(new Teams
        {
            TeamId = spare,
            JobId = JobId,
            DivId = DivId,
            DivRank = 5,
            TeamName = "Team 5",
            Active = true
        });
        await ctx.SaveChangesAsync();

        var act = async () => await Service(ctx).EnsureTeamMayLeavePoolAsync(spare, JobId, "deleted");
        await act.Should().NotThrowAsync();
    }

    [Fact(DisplayName = "Re-seating one pool does not touch another pool's games")]
    public async Task Reseat_IsScopedToItsOwnDivision()
    {
        var (ctx, byRank) = await Seed(DivId);

        // A second pool in the same job, seated and correct.
        var otherTeam = Guid.NewGuid();
        ctx.Teams.Add(new Teams
        {
            TeamId = otherTeam,
            JobId = JobId,
            DivId = OtherDivId,
            DivRank = 1,
            TeamName = "Other 1",
            Active = true
        });
        ctx.Schedule.Add(new Schedule
        {
            Gid = 99,
            JobId = JobId,
            DivId = OtherDivId,
            GDate = new DateTime(2026, 11, 22, 12, 0, 0),
            Season = "Fall",
            Year = "2026",
            LebUserId = UserId,
            T1Type = "T",
            T1No = 1,
            T1Id = otherTeam,
            T1Name = "Other 1",
            T2Type = "T",
            T2No = 2,
            T2Id = otherTeam,
            T2Name = "Other 1"
        });
        await ctx.SaveChangesAsync();

        await Service(ctx).ApplyRankChangeAsync(byRank[2], JobId, newRank: 3, newName: null, UserId);

        var untouched = ctx.Schedule.Single(s => s.Gid == 99);
        untouched.T1Id.Should().Be(otherTeam);
        untouched.DivId.Should().Be(OtherDivId);
    }

    [Fact(DisplayName = "A seat whose team is gone is left alone, never blanked")]
    public async Task SeatWithNoActiveTeam_IsNotWiped()
    {
        var (ctx, byRank) = await Seed(DivId);
        var team4 = byRank[4];

        // Legacy state: a team that is seated but no longer active. The guard prevents this
        // being created today, but old jobs carry it, and re-seating must not erase the games.
        ctx.Teams.Single(t => t.TeamId == team4).Active = false;
        await ctx.SaveChangesAsync();

        await Service(ctx).RenumberAndReseatAsync(DivId, JobId, UserId);

        // Every slot-4 game still names the team that was there. Erasing both sides of a real
        // game is worse than leaving a stale occupant that QA already reports.
        var slot4Games = ctx.Schedule
            .Where(s => s.DivId == DivId && (s.T1No == 4 || s.T2No == 4)).ToList();
        slot4Games.Should().NotBeEmpty();
        foreach (var g in slot4Games)
        {
            if (g.T1No == 4)
            {
                g.T1Id.Should().Be(team4);
                g.T1Name.Should().NotBeNullOrEmpty();
            }
            if (g.T2No == 4)
            {
                g.T2Id.Should().Be(team4);
                g.T2Name.Should().NotBeNullOrEmpty();
            }
        }
    }
}
