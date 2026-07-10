using FluentAssertions;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Scheduling;

/// <summary>
/// R1 seed resolution: a leaf bracket slot with a (division, rank) intent is
/// filled with the standings-ranked team once that pool is final, and never
/// clobbers an already-played bracket game.
/// </summary>
public class BracketSeedResolutionTests
{
    // Standings are supplied to the service via a delegate, so tests hand-build the
    // ranked shape directly rather than driving the full standings pipeline.
    private static StandingsDto Team(Guid teamId, string name, Guid divId) => new()
    {
        TeamId = teamId,
        TeamName = name,
        AgegroupName = "U10",
        DivName = "Gold",
        DivId = divId,
        Games = 0,
        Wins = 0,
        Losses = 0,
        Ties = 0,
        GoalsFor = 0,
        GoalsAgainst = 0,
        GoalDiffMax9 = 0,
        Points = 0,
        PointsPerGame = 0m,
        TiePoints = 0
    };

    private static StandingsByDivisionResponse Standings(params DivisionStandingsDto[] divs) =>
        new() { Divisions = divs.ToList(), SportName = "Soccer" };

    private static DivisionStandingsDto Division(Guid divId, params StandingsDto[] rankedTeams) =>
        new() { DivId = divId, AgegroupName = "U10", DivName = "Gold", Teams = rankedTeams.ToList() };

    private static void AddInstanceAndSeed(
        SqlDbContext ctx, Guid jobId, Guid agId, Guid divId,
        int gid, byte slot, Guid seedDivId, int seedRank, int instanceId = 1, int seedId = 1)
    {
        if (ctx.BracketInstances.Local.All(b => b.BracketInstanceId != instanceId))
        {
            ctx.BracketInstances.Add(new BracketInstances
            {
                BracketInstanceId = instanceId,
                JobId = jobId,
                AgegroupId = agId,
                DivId = divId,
                TemplateId = 1,
                Modified = DateTime.UtcNow,
                LebUserId = "seed"
            });
        }
        ctx.SeedAssignments.Add(new SeedAssignments
        {
            SeedAssignmentId = seedId,
            BracketInstanceId = instanceId,
            Gid = gid,
            TargetSlot = slot,
            SeedDivId = seedDivId,
            SeedRank = seedRank,
            Modified = DateTime.UtcNow,
            LebUserId = "seed"
        });
    }

    [Fact(DisplayName = "Complete pool → ranked team dropped into empty leaf slot")]
    public async Task Resolves_Into_Empty_Slot()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var eagles = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var hawks = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var field = b.AddField();

        // A scored pool game → division is complete (no unscored "T" games).
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            eagles.TeamId, hawks.TeamId, DateTime.Today, t1Score: 3, t2Score: 1);

        // Leaf championship game, T1 slot empty, awaiting Gold #1.
        var final = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, t1Type: "F", t2Type: "F");
        await b.SaveAsync();

        AddInstanceAndSeed(ctx, job.JobId, ag.AgegroupId, div.DivId,
            final.Gid, slot: 1, seedDivId: div.DivId, seedRank: 1);
        await ctx.SaveChangesAsync();

        var scheduleRepo = new ScheduleRepository(ctx);
        var svc = SchedulingTestFactory.SeedResolution(ctx, scheduleRepo);

        var standings = Standings(Division(div.DivId,
            Team(eagles.TeamId, "Eagles", div.DivId),
            Team(hawks.TeamId, "Hawks", div.DivId)));

        var count = await svc.ResolveJobAsync(job.JobId, "user", (_, _) => Task.FromResult(standings));

        count.Should().Be(1);
        var saved = await scheduleRepo.GetGameByIdAsync(final.Gid);
        saved!.T1Id.Should().Be(eagles.TeamId, "Gold #1 fills the T1 slot");
        saved.T1Name.Should().Be("Eagles");
    }

    [Fact(DisplayName = "Incomplete pool → seed waits, slot stays empty")]
    public async Task Waits_When_Pool_Incomplete()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var eagles = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var hawks = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var field = b.AddField();

        // An UNSCORED pool game → division rank is not yet final.
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            eagles.TeamId, hawks.TeamId, DateTime.Today);

        var final = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, t1Type: "F", t2Type: "F");
        await b.SaveAsync();

        AddInstanceAndSeed(ctx, job.JobId, ag.AgegroupId, div.DivId,
            final.Gid, slot: 1, seedDivId: div.DivId, seedRank: 1);
        await ctx.SaveChangesAsync();

        var scheduleRepo = new ScheduleRepository(ctx);
        var svc = SchedulingTestFactory.SeedResolution(ctx, scheduleRepo);

        var standings = Standings(Division(div.DivId,
            Team(eagles.TeamId, "Eagles", div.DivId),
            Team(hawks.TeamId, "Hawks", div.DivId)));

        var count = await svc.ResolveJobAsync(job.JobId, "user", (_, _) => Task.FromResult(standings));

        count.Should().Be(0);
        var saved = await scheduleRepo.GetGameByIdAsync(final.Gid);
        saved!.T1Id.Should().BeNull("rank not locked while a pool game is unscored");
    }

    [Fact(DisplayName = "Already-played bracket game is never clobbered")]
    public async Task Never_Clobbers_Played_Game()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var eagles = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var hawks = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var wolves = b.AddTeam(div.DivId, "Wolves", ag.AgegroupId);
        var field = b.AddField();

        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            eagles.TeamId, hawks.TeamId, DateTime.Today, t1Score: 3, t2Score: 1);

        // Leaf game already PLAYED (Wolves beat someone) — must not be overwritten.
        var final = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            wolves.TeamId, hawks.TeamId, DateTime.Today, t1Type: "F", t2Type: "F",
            t1Name: "Wolves", t2Name: "Hawks", t1Score: 5, t2Score: 2);
        await b.SaveAsync();

        AddInstanceAndSeed(ctx, job.JobId, ag.AgegroupId, div.DivId,
            final.Gid, slot: 1, seedDivId: div.DivId, seedRank: 1);
        await ctx.SaveChangesAsync();

        var scheduleRepo = new ScheduleRepository(ctx);
        var svc = SchedulingTestFactory.SeedResolution(ctx, scheduleRepo);

        var standings = Standings(Division(div.DivId,
            Team(eagles.TeamId, "Eagles", div.DivId),
            Team(hawks.TeamId, "Hawks", div.DivId)));

        var count = await svc.ResolveJobAsync(job.JobId, "user", (_, _) => Task.FromResult(standings));

        count.Should().Be(0);
        var saved = await scheduleRepo.GetGameByIdAsync(final.Gid);
        saved!.T1Id.Should().Be(wolves.TeamId, "a played result is the director's to correct, not ours to overwrite");
    }

    [Fact(DisplayName = "Cross-pool seed resolves from the other division's standings")]
    public async Task Resolves_Cross_Pool()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var gold = b.AddDivision(ag.AgegroupId, "Gold");
        var silver = b.AddDivision(ag.AgegroupId, "Silver");
        var eagles = b.AddTeam(gold.DivId, "Eagles", ag.AgegroupId);
        var sharks = b.AddTeam(silver.DivId, "Sharks", ag.AgegroupId);
        var minnows = b.AddTeam(silver.DivId, "Minnows", ag.AgegroupId);
        var field = b.AddField();

        // Both pools complete (scored pool games).
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, gold.DivId,
            eagles.TeamId, eagles.TeamId, DateTime.Today, t1Score: 1, t2Score: 0);
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, silver.DivId,
            sharks.TeamId, minnows.TeamId, DateTime.Today, t1Score: 4, t2Score: 0);

        // Cross-pool championship lives in Gold, but its T2 draws Silver #1.
        var final = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, gold.DivId,
            null, null, DateTime.Today, t1Type: "F", t2Type: "F");
        await b.SaveAsync();

        AddInstanceAndSeed(ctx, job.JobId, ag.AgegroupId, gold.DivId,
            final.Gid, slot: 2, seedDivId: silver.DivId, seedRank: 1);
        await ctx.SaveChangesAsync();

        var scheduleRepo = new ScheduleRepository(ctx);
        var svc = SchedulingTestFactory.SeedResolution(ctx, scheduleRepo);

        var standings = Standings(
            Division(gold.DivId, Team(eagles.TeamId, "Eagles", gold.DivId)),
            Division(silver.DivId,
                Team(sharks.TeamId, "Sharks", silver.DivId),
                Team(minnows.TeamId, "Minnows", silver.DivId)));

        var count = await svc.ResolveJobAsync(job.JobId, "user", (_, _) => Task.FromResult(standings));

        count.Should().Be(1);
        var saved = await scheduleRepo.GetGameByIdAsync(final.Gid);
        saved!.T2Id.Should().Be(sharks.TeamId, "Silver #1 fills the cross-pool T2 slot");
        saved.T2Name.Should().Be("Sharks");
    }
}
