#pragma warning disable S2234 // Tests intentionally swap home/away team IDs
using FluentAssertions;
using TSIC.API.Services.Scheduling;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Events.Schedule;

public class ScheduleGamesTests
{
    private static (ViewScheduleService svc, MobileDataBuilder builder, Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId)
        CreateService()
    {
        var ctx = DbContextFactory.Create();
        var builder = new MobileDataBuilder(ctx);
        var scheduleRepo = new ScheduleRepository(ctx);
        var teamRepo = new TeamRepository(ctx);
        var svc = new ViewScheduleService(scheduleRepo, teamRepo);
        var job = builder.AddJob();
        return (svc, builder, ctx, job.JobId);
    }

    private static (MobileDataBuilder b, Guid jobId, Guid leagueId, Guid agId, Guid divId, Guid fieldId, Guid t1Id, Guid t2Id)
        SeedStandardSchedule(MobileDataBuilder b, Guid jobId)
    {
        var league = b.AddLeague(jobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var t1 = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var t2 = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var field = b.AddField("Field 1", "Allentown", "PA");
        return (b, jobId, league.LeagueId, ag.AgegroupId, div.DivId, field.FieldId, t1.TeamId, t2.TeamId);
    }

    [Fact(DisplayName = "Unfiltered → returns all games")]
    public async Task GetGames_Unfiltered_ReturnsAll()
    {
        var (svc, b, _, jobId) = CreateService();
        var (_, _, leagueId, agId, divId, fieldId, t1Id, t2Id) = SeedStandardSchedule(b, jobId);
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, DateTime.Today, agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Hawks");
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t2Id, t2Id: t1Id, DateTime.Today.AddDays(1), rnd: 2, agegroupName: "U10", divName: "Gold", t1Name: "Hawks", t2Name: "Eagles");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest());

        result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Filter by teamIds → returns only matching games")]
    public async Task GetGames_FilterByTeam_ReturnsMatching()
    {
        var (svc, b, _, jobId) = CreateService();
        var (_, _, leagueId, agId, divId, fieldId, t1Id, t2Id) = SeedStandardSchedule(b, jobId);
        var t3 = b.AddTeam(divId, "Wolves", agId);
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, DateTime.Today, agegroupName: "U10", divName: "Gold");
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t3.TeamId, DateTime.Today, rnd: 2, agegroupName: "U10", divName: "Gold");
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t2Id, t2Id: t3.TeamId, DateTime.Today, rnd: 3, agegroupName: "U10", divName: "Gold");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest { TeamIds = [t1Id] });

        result.Should().HaveCount(2, "team appears as T1 or T2");
    }

    [Fact(DisplayName = "Filter by gameDays → returns only matching dates")]
    public async Task GetGames_FilterByGameDay_ReturnsMatching()
    {
        var (svc, b, _, jobId) = CreateService();
        var (_, _, leagueId, agId, divId, fieldId, t1Id, t2Id) = SeedStandardSchedule(b, jobId);
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, today, agegroupName: "U10", divName: "Gold");
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t2Id, t2Id: t1Id, tomorrow, rnd: 2, agegroupName: "U10", divName: "Gold");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest { GameDays = [today] });

        result.Should().HaveCount(1);
        result[0].GDate.Date.Should().Be(today);
    }

    [Fact(DisplayName = "Filter by unscoredOnly → excludes scored games")]
    public async Task GetGames_UnscoredOnly_ExcludesScored()
    {
        var (svc, b, _, jobId) = CreateService();
        var (_, _, leagueId, agId, divId, fieldId, t1Id, t2Id) = SeedStandardSchedule(b, jobId);
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, DateTime.Today, agegroupName: "U10", divName: "Gold", t1Score: 3, t2Score: 1);
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t2Id, t2Id: t1Id, DateTime.Today, rnd: 2, agegroupName: "U10", divName: "Gold");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest { UnscoredOnly = true });

        result.Should().HaveCount(1, "scored game excluded");
        result[0].T1Score.Should().BeNull();
    }

    [Fact(DisplayName = "DivName populated on each game")]
    public async Task GetGames_DivNamePopulated()
    {
        var (svc, b, _, jobId) = CreateService();
        var (_, _, leagueId, agId, divId, fieldId, t1Id, t2Id) = SeedStandardSchedule(b, jobId);
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, DateTime.Today, agegroupName: "U10", divName: "Gold");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest());

        result[0].DivName.Should().Be("Gold");
    }

    [Fact(DisplayName = "T1Record/T2Record populated from pool-play stats")]
    public async Task GetGames_RecordsPopulated()
    {
        var (svc, b, _, jobId) = CreateService();
        var (_, _, leagueId, agId, divId, fieldId, t1Id, t2Id) = SeedStandardSchedule(b, jobId);
        // Game 1: Eagles 3, Hawks 1 (Eagles win)
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, DateTime.Today,
            agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Hawks",
            t1Score: 3, t2Score: 1);
        // Game 2: unscored — records should still show from game 1
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, DateTime.Today.AddDays(1), rnd: 2,
            agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Hawks");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest());

        // The unscored game should show records from the scored game
        var unscoredGame = result.First(g => g.T1Score == null);
        unscoredGame.T1Record.Should().Be("1-0-0", "Eagles: 1 win");
        unscoredGame.T2Record.Should().Be("0-1-0", "Hawks: 1 loss");
    }

    [Fact(DisplayName = "AgDiv format is Agegroup:Division")]
    public async Task GetGames_AgDivFormat()
    {
        var (svc, b, _, jobId) = CreateService();
        var (_, _, leagueId, agId, divId, fieldId, t1Id, t2Id) = SeedStandardSchedule(b, jobId);
        b.AddGame(jobId, leagueId, fieldId: fieldId, agegroupId: agId, divId: divId, t1Id: t1Id, t2Id: t2Id, DateTime.Today,
            agegroupName: "U10", divName: "Gold");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest());

        result[0].AgDiv.Should().Be("U10:Gold");
    }

    [Fact(DisplayName = "FAddress built from field address parts")]
    public async Task GetGames_FAddressBuiltFromField()
    {
        var (svc, b, _, jobId) = CreateService();
        var league = b.AddLeague(jobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var t1 = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId);
        var t2 = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId);
        var field = b.AddField("Main Field", "Allentown", "PA", address: "123 Main St", zip: "18101");
        b.AddGame(jobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId, t1.TeamId, t2.TeamId, DateTime.Today,
            agegroupName: "U10", divName: "Gold");
        await b.SaveAsync();

        var result = await svc.GetGamesAsync(jobId, new ScheduleFilterRequest());

        result[0].FAddress.Should().Contain("123 Main St");
        result[0].FAddress.Should().Contain("Allentown");
        result[0].FAddress.Should().Contain("PA");
    }
}
