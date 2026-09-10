using FluentAssertions;
using TSIC.API.Services.Teams;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;
using Xunit;

namespace TSIC.Tests.Scheduling;

/// <summary>
/// The "Teams Not Matching Ranks" card on Schedule QA. It compares each round-robin game's
/// slot number (T1_No / T2_No) against the actual DivRank of the team sitting in that slot,
/// and lists one row per team per game that disagrees.
///
/// It was the only thing in the product that noticed the Live Love Lax 2031 defect, and it had
/// no test of its own. These walk the full repair loop end to end: clean board reads 0, ranks
/// move without the board following and it reads the mismatch, the re-seat takes it back to 0.
/// </summary>
public class ScheduleQaRankMismatchTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid DivId = Guid.NewGuid();
    private const string UserId = "test-admin";

    /// <summary>
    /// Four teams at ranks 1-4 and the six games of a four-team round robin, correctly seated.
    /// Distinct times so the other QA checks stay quiet and only the rank card can speak.
    /// </summary>
    private static async Task<(SqlDbContext ctx, Dictionary<int, Guid> teamsByRank)> Seed()
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
                DivId = DivId,
                DivRank = rank,
                TeamName = $"Team {rank}",
                Active = true
            });
        }

        var pairings = new[] { (2, 1), (4, 3), (1, 3), (2, 4), (4, 1), (3, 2) };
        var gid = 1;
        var slot = 0;
        foreach (var (t1, t2) in pairings)
        {
            ctx.Schedule.Add(new Schedule
            {
                Gid = gid++,
                JobId = JobId,
                DivId = DivId,
                DivName = "Knowledge",
                AgegroupName = "2031",
                FName = "Field 1",
                GDate = new DateTime(2026, 11, 22, 9, 0, 0).AddMinutes(90 * slot++),
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

    /// <summary>
    /// What the pool screen used to do: move the ranks and leave the board alone. This is the
    /// defect being repaired, reproduced exactly — no schedule row is touched.
    /// </summary>
    private static async Task SwapRanksWithoutReseating(SqlDbContext ctx, Guid a, Guid b)
    {
        var teamA = ctx.Teams.Single(t => t.TeamId == a);
        var teamB = ctx.Teams.Single(t => t.TeamId == b);
        (teamA.DivRank, teamB.DivRank) = (teamB.DivRank, teamA.DivRank);
        await ctx.SaveChangesAsync();
    }

    [Fact(DisplayName = "A correctly seated board reports no rank mismatches")]
    public async Task CleanBoard_ReportsZero()
    {
        var (ctx, _) = await Seed();

        var qa = await new AutoBuildRepository(ctx).RunQaValidationAsync(JobId);

        qa.RankMismatches.Should().BeEmpty();
        qa.TotalGames.Should().Be(6);
    }

    [Fact(DisplayName = "Ranks moved without the board following: QA names both teams, every game")]
    public async Task RanksMovedWithoutReseat_IsReported()
    {
        var (ctx, byRank) = await Seed();

        await SwapRanksWithoutReseating(ctx, byRank[2], byRank[3]);

        var qa = await new AutoBuildRepository(ctx).RunQaValidationAsync(JobId);

        // Each of the two teams plays three games, and in every one of them its slot number now
        // disagrees with its rank. One row per team per game — six.
        qa.RankMismatches.Should().HaveCount(6);
        qa.RankMismatches.Select(m => m.TeamName).Distinct()
            .Should().BeEquivalentTo("Team 2", "Team 3");

        // Team 2 still sits in slot 2 everywhere but now holds rank 3, and vice versa. That
        // pairing is the whole signature of the defect.
        qa.RankMismatches.Where(m => m.TeamName == "Team 2")
            .Should().OnlyContain(m => m.ScheduleNo == 2 && m.ActualDivRank == 3);
        qa.RankMismatches.Where(m => m.TeamName == "Team 3")
            .Should().OnlyContain(m => m.ScheduleNo == 3 && m.ActualDivRank == 2);

        // Nothing else is wrong with this board — the rank card is the only one complaining.
        qa.TeamDoubleBookings.Should().BeEmpty();
        qa.FieldDoubleBookings.Should().BeEmpty();
        qa.UnscheduledTeams.Should().BeEmpty();
    }

    [Fact(DisplayName = "Re-seating the division clears the QA error")]
    public async Task Reseat_ClearsTheMismatch()
    {
        var (ctx, byRank) = await Seed();
        await SwapRanksWithoutReseating(ctx, byRank[2], byRank[3]);

        var qa = new AutoBuildRepository(ctx);
        (await qa.RunQaValidationAsync(JobId)).RankMismatches.Should().HaveCount(6);

        var teamRepo = new TeamRepository(ctx);
        var scheduleRepo = new ScheduleRepository(ctx);
        var seating = new TeamSeatingService(
            teamRepo, scheduleRepo,
            new TeamRenameService(teamRepo, new ClubTeamRepository(ctx), scheduleRepo),
            new LeagueRepository(ctx));

        await seating.RenumberAndReseatAsync(DivId, JobId, UserId);

        (await qa.RunQaValidationAsync(JobId)).RankMismatches.Should().BeEmpty();
    }

    [Fact(DisplayName = "Bracket and consolation games are not judged against pool ranks")]
    public async Task NonRoundRobinGames_AreNotFlagged()
    {
        var (ctx, byRank) = await Seed();

        // A bracket game seeded from finish position, not pool rank. Its slot numbers mean
        // something else entirely, so holding them to DivRank would be a false alarm.
        ctx.Schedule.Add(new Schedule
        {
            Gid = 100,
            JobId = JobId,
            DivId = DivId,
            DivName = "Knowledge",
            AgegroupName = "2031",
            FName = "Field 2",
            GDate = new DateTime(2026, 11, 23, 9, 0, 0),
            Season = "Fall",
            Year = "2026",
            LebUserId = UserId,
            T1Type = "X",
            T1No = 4,
            T1Id = byRank[1],
            T1Name = "Team 1",
            T2Type = "X",
            T2No = 3,
            T2Id = byRank[2],
            T2Name = "Team 2"
        });
        await ctx.SaveChangesAsync();

        var qa = await new AutoBuildRepository(ctx).RunQaValidationAsync(JobId);

        qa.RankMismatches.Should().BeEmpty();
    }
}
