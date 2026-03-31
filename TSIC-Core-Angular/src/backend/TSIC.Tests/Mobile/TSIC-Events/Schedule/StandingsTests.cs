using FluentAssertions;
using TSIC.API.Services.Scheduling;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Events.Schedule;

public class StandingsTests
{
    private static (ViewScheduleService svc, MobileDataBuilder builder, Guid jobId)
        CreateServiceWithScoredGames(string sportName = "Soccer")
    {
        var ctx = DbContextFactory.Create();
        var builder = new MobileDataBuilder(ctx);

        // Override sport name if needed
        var sportId = Guid.NewGuid();
        ctx.Sports.Add(new Domain.Entities.Sports { SportId = sportId, SportName = sportName, Ai = 2, Modified = DateTime.UtcNow });

        var job = builder.AddJob(sportId: sportId);
        var league = builder.AddLeague(job.JobId, sportId);
        var ag = builder.AddAgegroup(league.LeagueId, "U10");
        var div = builder.AddDivision(ag.AgegroupId, "Gold");
        var t1 = builder.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var t2 = builder.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var t3 = builder.AddTeam(div.DivId, "Wolves", ag.AgegroupId);
        var field = builder.AddField();

        // Eagles 3-1 Hawks (Eagles win)
        builder.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            t1.TeamId, t2.TeamId, DateTime.Today,
            agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Hawks",
            t1Score: 3, t2Score: 1);

        // Hawks 2-2 Wolves (Tie)
        builder.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            t2.TeamId, t3.TeamId, DateTime.Today, rnd: 2,
            agegroupName: "U10", divName: "Gold", t1Name: "Hawks", t2Name: "Wolves",
            t1Score: 2, t2Score: 2);

        // Eagles 1-0 Wolves (Eagles win)
        builder.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            t1.TeamId, t3.TeamId, DateTime.Today, rnd: 3,
            agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Wolves",
            t1Score: 1, t2Score: 0);

        var scheduleRepo = new ScheduleRepository(ctx);
        var teamRepo = new TeamRepository(ctx);
        var svc = new ViewScheduleService(scheduleRepo, teamRepo);

        return (svc, builder, job.JobId);
    }

    [Fact(DisplayName = "Basic standings → W-L-T calculated correctly")]
    public async Task Standings_WLT_Correct()
    {
        var (svc, b, jobId) = CreateServiceWithScoredGames();
        await b.SaveAsync();

        var result = await svc.GetStandingsAsync(jobId, new ScheduleFilterRequest());

        var eagles = result.Divisions[0].Teams.First(t => t.TeamName == "Eagles");
        eagles.Wins.Should().Be(2);
        eagles.Losses.Should().Be(0);
        eagles.Ties.Should().Be(0);
        eagles.Games.Should().Be(2);

        var hawks = result.Divisions[0].Teams.First(t => t.TeamName == "Hawks");
        hawks.Wins.Should().Be(0);
        hawks.Losses.Should().Be(1);
        hawks.Ties.Should().Be(1);
    }

    [Fact(DisplayName = "Soccer sort → Points DESC, then Wins, then GoalDiff")]
    public async Task Standings_SoccerSort()
    {
        var (svc, b, jobId) = CreateServiceWithScoredGames("Soccer");
        await b.SaveAsync();

        var result = await svc.GetStandingsAsync(jobId, new ScheduleFilterRequest());
        var teams = result.Divisions[0].Teams;

        // Eagles: 2W 0L 0T = 6pts, Hawks: 0W 1L 1T = 1pt, Wolves: 0W 1L 1T = 1pt
        teams[0].TeamName.Should().Be("Eagles", "highest points first");
        teams[0].Points.Should().Be(6);
    }

    [Fact(DisplayName = "Lacrosse sort → Wins DESC, then Losses, then GoalDiff")]
    public async Task Standings_LacrosseSort()
    {
        var (svc, b, jobId) = CreateServiceWithScoredGames("Lacrosse");
        await b.SaveAsync();

        var result = await svc.GetStandingsAsync(jobId, new ScheduleFilterRequest());
        var teams = result.Divisions[0].Teams;

        teams[0].TeamName.Should().Be("Eagles", "most wins first");
    }

    [Fact(DisplayName = "GoalDiffMax9 capped at ±9")]
    public async Task Standings_GoalDiffCapped()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var t1 = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var t2 = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var field = b.AddField();

        // Blowout: 15-0
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            t1.TeamId, t2.TeamId, DateTime.Today,
            agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Hawks",
            t1Score: 15, t2Score: 0);
        await b.SaveAsync();

        var scheduleRepo = new ScheduleRepository(ctx);
        var teamRepo = new TeamRepository(ctx);
        var svc = new ViewScheduleService(scheduleRepo, teamRepo);

        var result = await svc.GetStandingsAsync(job.JobId, new ScheduleFilterRequest());

        var eagles = result.Divisions[0].Teams.First(t => t.TeamName == "Eagles");
        eagles.GoalDiffMax9.Should().Be(9, "capped at +9");
        eagles.GoalsFor.Should().Be(15, "actual goals not capped");

        var hawks = result.Divisions[0].Teams.First(t => t.TeamName == "Hawks");
        hawks.GoalDiffMax9.Should().Be(-9, "capped at -9");
    }

    [Fact(DisplayName = "TiePoints = number of ties")]
    public async Task Standings_TiePoints()
    {
        var (svc, b, jobId) = CreateServiceWithScoredGames();
        await b.SaveAsync();

        var result = await svc.GetStandingsAsync(jobId, new ScheduleFilterRequest());

        var hawks = result.Divisions[0].Teams.First(t => t.TeamName == "Hawks");
        hawks.TiePoints.Should().Be(1, "1 tie = 1 tie point");
        hawks.Ties.Should().Be(1);

        var eagles = result.Divisions[0].Teams.First(t => t.TeamName == "Eagles");
        eagles.TiePoints.Should().Be(0, "no ties");
    }

    [Fact(DisplayName = "Unscored teams appear as 0-0-0")]
    public async Task Standings_UnscoredTeamsSeeded()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var t1 = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var t2 = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var field = b.AddField();

        // Unscored game
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            t1.TeamId, t2.TeamId, DateTime.Today,
            agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Hawks");
        await b.SaveAsync();

        var scheduleRepo = new ScheduleRepository(ctx);
        var teamRepo = new TeamRepository(ctx);
        var svc = new ViewScheduleService(scheduleRepo, teamRepo);

        var result = await svc.GetStandingsAsync(job.JobId, new ScheduleFilterRequest());

        result.Divisions[0].Teams.Should().HaveCount(2, "both teams appear");
        result.Divisions[0].Teams.Should().AllSatisfy(t =>
        {
            t.Games.Should().Be(0);
            t.Wins.Should().Be(0);
            t.Losses.Should().Be(0);
            t.Ties.Should().Be(0);
        });
    }
}
