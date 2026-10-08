using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Services.Scheduling;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;
using Xunit;

namespace TSIC.Tests.Scheduling;

/// <summary>
/// A director overrides a pool game in Edit Game to bring in a team from another pool. A schedule
/// row's divID is the GAME's pool, not every team's, so:
/// <list type="bullet">
/// <item>standings must place each team by its OWN pool, or the visitor is listed (and seeded) in
///       the host pool;</item>
/// <item>the override must record the visitor's own pool and rank in its slot (div2ID + T2_No), or
///       the next re-seat of the host pool silently puts the original team back.</item>
/// </list>
/// Refused: a team change on a scored game, two visitors in one game, a team from another age
/// group, a team with no active pool.
///
/// Layout: U10 pools A and B (A1-A3, B1-B3 at ranks 1-3), U12 pool C (C1). The game under test is
/// "the override game", Pool A's A1 v A3 — scheduled FIRST in the event, so the visitor's earliest
/// game is the cross-pool one (the order that used to file it under the host pool).
/// </summary>
public class CrossPoolOverrideTests
{
    private const string UserId = "test-admin";

    private sealed record World(
        SqlDbContext Ctx, ViewScheduleService Svc, ScheduleRepository ScheduleRepo, Guid JobId,
        Guid PoolA, Guid PoolB, Dictionary<string, Guid> Team, int OverrideGid, int ScoredGid);

    private static async Task<World> SeedAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var u10 = b.AddAgegroup(league.LeagueId, "U10");
        var u12 = b.AddAgegroup(league.LeagueId, "U12");
        var poolA = b.AddDivision(u10.AgegroupId, "A");
        var poolB = b.AddDivision(u10.AgegroupId, "B");
        var poolC = b.AddDivision(u12.AgegroupId, "C");
        var field = b.AddField();

        var team = new Dictionary<string, Guid>();
        void AddTeams(Divisions div, Agegroups ag, string prefix, int count)
        {
            for (var rank = 1; rank <= count; rank++)
            {
                var t = b.AddTeam(div.DivId, $"{prefix}{rank}", ag.AgegroupId, job.JobId, league.LeagueId);
                t.DivRank = rank;
                team[$"{prefix}{rank}"] = t.TeamId;
            }
        }
        AddTeams(poolA, u10, "A", 3);
        AddTeams(poolB, u10, "B", 3);
        AddTeams(poolC, u12, "C", 1);

        // Pool round robins, seated the way the scheduler writes them (div2ID = divID).
        var day = new DateTime(2026, 11, 21);
        Schedule Game(Divisions div, Agegroups ag, string prefix, int t1No, int t2No, int hour, int? s1 = null, int? s2 = null)
        {
            var g = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
                team[$"{prefix}{t1No}"], team[$"{prefix}{t2No}"], day.AddHours(hour),
                t1Name: $"{prefix}{t1No}", t2Name: $"{prefix}{t2No}",
                agegroupName: ag.AgegroupName, divName: div.DivName, t1Score: s1, t2Score: s2);
            g.T1No = t1No;
            g.T2No = (byte)t2No;
            g.Div2Id = div.DivId;
            g.Div2Name = div.DivName;
            return g;
        }

        var overrideGame = Game(poolA, u10, "A", 1, 3, hour: 8);          // the event's first game
        var scoredGame = Game(poolA, u10, "A", 1, 2, hour: 9, s1: 3, s2: 1);
        Game(poolA, u10, "A", 2, 3, hour: 10);
        Game(poolB, u10, "B", 1, 2, hour: 9);
        Game(poolB, u10, "B", 2, 3, hour: 10);
        Game(poolB, u10, "B", 1, 3, hour: 11);

        await b.SaveAsync();

        var scheduleRepo = new ScheduleRepository(ctx);
        var svc = SchedulingTestFactory.ViewSchedule(ctx, scheduleRepo, new TeamRepository(ctx));
        await svc.RebuildTeamRecordsAsync(job.JobId);

        return new World(ctx, svc, scheduleRepo, job.JobId, poolA.DivId, poolB.DivId, team,
            overrideGame.Gid, scoredGame.Gid);
    }

    private static async Task<Schedule> GameAsync(World w, int gid) =>
        await w.Ctx.Schedule.AsNoTracking().SingleAsync(g => g.Gid == gid);

    /// <summary>What the modal sends: both team IDs every time, plus the edited fields.</summary>
    private static async Task EditTeamsAsync(World w, int gid, Guid t1Id, Guid t2Id)
    {
        var g = await GameAsync(w, gid);
        await w.Svc.EditGameAsync(w.JobId, UserId, new EditGameRequest
        {
            Gid = gid,
            T1Id = t1Id,
            T2Id = t2Id,
            T1Name = "edited-1",
            T2Name = "edited-2",
            T1Score = g.T1Score,
            T2Score = g.T2Score,
            GStatusCode = g.GStatusCode ?? 1
        });
    }

    private static async Task<List<string>> PoolTeamNamesAsync(World w, Guid divId, ScheduleFilterRequest filter)
    {
        var standings = await w.Svc.GetStandingsAsync(w.JobId, filter);
        return standings.Divisions
            .Where(d => d.DivId == divId)
            .SelectMany(d => d.Teams.Select(t => t.TeamId))
            .Select(id => w.Team.Single(kv => kv.Value == id).Key)
            .OrderBy(n => n)
            .ToList();
    }

    // ── Standings ──

    [Fact(DisplayName = "Standings: pool-scoped request leaves the visitor out of the host pool")]
    public async Task Standings_PoolScope_ExcludesVisitor()
    {
        var w = await SeedAsync();
        await EditTeamsAsync(w, w.OverrideGid, w.Team["A1"], w.Team["B2"]);

        var standings = await w.Svc.GetStandingsAsync(w.JobId,
            new ScheduleFilterRequest { DivisionIds = [w.PoolA] });

        standings.Divisions.Should().ContainSingle("a Pool A request returns Pool A only — no stray visitor block");
        (await PoolTeamNamesAsync(w, w.PoolA, new ScheduleFilterRequest { DivisionIds = [w.PoolA] }))
            .Should().Equal("A1", "A2", "A3");
    }

    [Fact(DisplayName = "Standings: an unstamped legacy override still lists the visitor in its own pool only")]
    public async Task Standings_AllPools_LegacyUnstampedRow_VisitorInOwnPool()
    {
        var w = await SeedAsync();

        // How a pre-fix override looks: only T2_ID changed, slot still says Pool A rank 3.
        var row = await w.Ctx.Schedule.SingleAsync(g => g.Gid == w.OverrideGid);
        row.T2Id = w.Team["B2"];
        await w.Ctx.SaveChangesAsync();
        w.Ctx.ChangeTracker.Clear();

        var all = new ScheduleFilterRequest();
        (await PoolTeamNamesAsync(w, w.PoolA, all)).Should().Equal("A1", "A2", "A3");
        // The cross-pool game is B2's earliest, which used to file B2 under Pool A.
        (await PoolTeamNamesAsync(w, w.PoolB, all)).Should().Equal("B1", "B2", "B3");
    }

    // ── Slot stamping ──

    [Fact(DisplayName = "Edit Game: visitor in T2 records its own pool and rank")]
    public async Task EditGame_T2Visitor_StampsDiv2AndRank()
    {
        var w = await SeedAsync();
        await EditTeamsAsync(w, w.OverrideGid, w.Team["A1"], w.Team["B2"]);

        var g = await GameAsync(w, w.OverrideGid);
        g.T2Id.Should().Be(w.Team["B2"]);
        g.T2No.Should().Be((byte)2);
        g.Div2Id.Should().Be(w.PoolB);
        g.Div2Name.Should().Be("B");
        g.DivId.Should().Be(w.PoolA, "the game still belongs to the host pool");
        g.T1No.Should().Be(1);
    }

    [Fact(DisplayName = "Edit Game: the override survives a re-seat of either pool")]
    public async Task EditGame_Override_SurvivesReseatOfBothPools()
    {
        var w = await SeedAsync();
        await EditTeamsAsync(w, w.OverrideGid, w.Team["A1"], w.Team["B2"]);
        w.Ctx.ChangeTracker.Clear();

        await w.ScheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(w.PoolA, w.JobId, UserId);
        await w.ScheduleRepo.SynchronizeScheduleTeamAssignmentsForDivisionAsync(w.PoolB, w.JobId, UserId);

        var g = await GameAsync(w, w.OverrideGid);
        g.T1Id.Should().Be(w.Team["A1"]);
        g.T2Id.Should().Be(w.Team["B2"], "a re-seat must not put A3 back");
    }

    [Fact(DisplayName = "Edit Game: visitor entered in T1 trades places with T2")]
    public async Task EditGame_T1Visitor_SwapsToT2()
    {
        var w = await SeedAsync();
        await EditTeamsAsync(w, w.OverrideGid, w.Team["B2"], w.Team["A3"]);

        var g = await GameAsync(w, w.OverrideGid);
        g.T1Id.Should().Be(w.Team["A3"]);
        g.T1No.Should().Be(3);
        g.T1Name.Should().Be("edited-2", "the whole side moves, name included");
        g.T2Id.Should().Be(w.Team["B2"]);
        g.T2No.Should().Be((byte)2);
        g.T2Name.Should().Be("edited-1");
        g.Div2Id.Should().Be(w.PoolB);
    }

    [Fact(DisplayName = "Edit Game: putting the original team back restores the host-pool slot")]
    public async Task EditGame_Undo_RestoresHostSlot()
    {
        var w = await SeedAsync();
        await EditTeamsAsync(w, w.OverrideGid, w.Team["A1"], w.Team["B2"]);
        w.Ctx.ChangeTracker.Clear();
        await EditTeamsAsync(w, w.OverrideGid, w.Team["A1"], w.Team["A3"]);

        var g = await GameAsync(w, w.OverrideGid);
        g.T2Id.Should().Be(w.Team["A3"]);
        g.T2No.Should().Be((byte)3);
        g.Div2Id.Should().Be(w.PoolA);
        g.Div2Name.Should().Be("A");
    }

    // ── Refusals ──

    [Fact(DisplayName = "Edit Game: refuses a team change on a scored game")]
    public async Task EditGame_ScoredGame_TeamChangeRefused()
    {
        var w = await SeedAsync();

        var act = () => EditTeamsAsync(w, w.ScoredGid, w.Team["A1"], w.Team["A3"]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*has a score*");
        (await GameAsync(w, w.ScoredGid)).T2Id.Should().Be(w.Team["A2"]);
    }

    [Fact(DisplayName = "Edit Game: a scored game still saves when the same teams are echoed")]
    public async Task EditGame_ScoredGame_SameTeamsEchoed_Saves()
    {
        var w = await SeedAsync();

        await w.Svc.EditGameAsync(w.JobId, UserId, new EditGameRequest
        {
            Gid = w.ScoredGid,
            T1Id = w.Team["A1"],
            T2Id = w.Team["A2"],
            T1Score = 3,
            T2Score = 1,
            T1Ann = "weather delay",
            GStatusCode = 6
        });

        (await GameAsync(w, w.ScoredGid)).T1Ann.Should().Be("weather delay");
    }

    [Fact(DisplayName = "Edit Game: refuses two teams from outside the game's pool")]
    public async Task EditGame_TwoVisitors_Refused()
    {
        var w = await SeedAsync();

        var act = () => EditTeamsAsync(w, w.OverrideGid, w.Team["B1"], w.Team["B2"]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Only one team*");
        (await GameAsync(w, w.OverrideGid)).T1Id.Should().Be(w.Team["A1"]);
    }

    [Fact(DisplayName = "Edit Game: refuses a team from another age group")]
    public async Task EditGame_OtherAgegroup_Refused()
    {
        var w = await SeedAsync();

        var act = () => EditTeamsAsync(w, w.OverrideGid, w.Team["A1"], w.Team["C1"]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*own age group*");
        (await GameAsync(w, w.OverrideGid)).T2Id.Should().Be(w.Team["A3"]);
    }

    [Fact(DisplayName = "Edit Game: refuses an inactive team")]
    public async Task EditGame_InactiveTeam_Refused()
    {
        var w = await SeedAsync();
        var b3 = await w.Ctx.Teams.SingleAsync(t => t.TeamId == w.Team["B3"]);
        b3.Active = false;
        await w.Ctx.SaveChangesAsync();
        w.Ctx.ChangeTracker.Clear();

        var act = () => EditTeamsAsync(w, w.OverrideGid, w.Team["A1"], w.Team["B3"]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active pool*");
    }
}
