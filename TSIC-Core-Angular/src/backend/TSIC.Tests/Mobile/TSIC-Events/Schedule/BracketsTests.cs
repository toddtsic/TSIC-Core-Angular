using FluentAssertions;
using TSIC.API.Services.Scheduling;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.TSIC_Events.Schedule;

public class BracketsTests
{
    [Fact(DisplayName = "Bracket games grouped by agegroup")]
    public async Task Brackets_GroupedByAgegroup()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag1 = b.AddAgegroup(league.LeagueId, "U10");
        var ag2 = b.AddAgegroup(league.LeagueId, "U12");
        var div1 = b.AddDivision(ag1.AgegroupId, "Gold");
        var div2 = b.AddDivision(ag2.AgegroupId, "Gold");
        var field = b.AddField();

        // Bracket game in U10
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag1.AgegroupId, div1.DivId,
            null, null, DateTime.Today, rnd: 1, t1Type: "S", t2Type: "S",
            agegroupName: "U10", divName: "Gold");

        // Bracket game in U12
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag2.AgegroupId, div2.DivId,
            null, null, DateTime.Today, rnd: 1, t1Type: "S", t2Type: "S",
            agegroupName: "U12", divName: "Gold");
        await b.SaveAsync();

        var svc = new ViewScheduleService(new ScheduleRepository(ctx), new TeamRepository(ctx));
        var result = await svc.GetBracketsAsync(job.JobId, new ScheduleFilterRequest());

        result.Should().HaveCount(2);
        result.Select(r => r.AgegroupName).Should().Contain("U10").And.Contain("U12");
    }

    [Fact(DisplayName = "Champion determined from Finals winner")]
    public async Task Brackets_ChampionFromFinals()
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

        // Finals game: Eagles 2, Hawks 1
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            t1.TeamId, t2.TeamId, DateTime.Today, rnd: 1, t1Type: "F", t2Type: "F",
            agegroupName: "U10", divName: "", t1Name: "Eagles", t2Name: "Hawks",
            t1Score: 2, t2Score: 1);
        await b.SaveAsync();

        var svc = new ViewScheduleService(new ScheduleRepository(ctx), new TeamRepository(ctx));
        var result = await svc.GetBracketsAsync(job.JobId, new ScheduleFilterRequest());

        result.Should().HaveCount(1);
        result[0].Champion.Should().Be("Eagles");
    }

    [Fact(DisplayName = "T1Css/T2Css set to winner/loser/pending")]
    public async Task Brackets_CssClasses()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var field = b.AddField();

        // Scored semi: T1 wins
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, rnd: 1, t1Type: "S", t2Type: "S",
            agegroupName: "U10", divName: "", t1Name: "A", t2Name: "B",
            t1Score: 3, t2Score: 1);

        // Unscored final: pending
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, rnd: 2, t1Type: "F", t2Type: "F",
            agegroupName: "U10", divName: "", t1Name: "TBD", t2Name: "TBD");
        await b.SaveAsync();

        var svc = new ViewScheduleService(new ScheduleRepository(ctx), new TeamRepository(ctx));
        var result = await svc.GetBracketsAsync(job.JobId, new ScheduleFilterRequest());

        var semi = result[0].Matches.First(m => m.RoundType == "S");
        semi.T1Css.Should().Be("winner");
        semi.T2Css.Should().Be("loser");

        var final = result[0].Matches.First(m => m.RoundType == "F");
        final.T1Css.Should().Be("pending");
        final.T2Css.Should().Be("pending");
    }

    [Fact(DisplayName = "ParentGid links child → parent game")]
    public async Task Brackets_ParentGidLinksTree()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var field = b.AddField();

        // Semi 1 (rnd 1, T1No=1)
        var semi1 = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, rnd: 1, t1Type: "S", t2Type: "S",
            agegroupName: "U10", divName: "");
        semi1.T1No = 1;
        semi1.T2No = 4;

        // Semi 2 (rnd 1, T1No=2)
        var semi2 = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, rnd: 1, t1Type: "S", t2Type: "S",
            agegroupName: "U10", divName: "");
        semi2.T1No = 2;
        semi2.T2No = 3;

        // Final (rnd 2, T1No=1, T2No=2 — winners of semis)
        var final = b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, rnd: 2, t1Type: "F", t2Type: "F",
            agegroupName: "U10", divName: "");
        final.T1No = 1;
        final.T2No = 2;

        await b.SaveAsync();

        var svc = new ViewScheduleService(new ScheduleRepository(ctx), new TeamRepository(ctx));
        var result = await svc.GetBracketsAsync(job.JobId, new ScheduleFilterRequest());

        var matches = result[0].Matches;
        var semiMatch1 = matches.First(m => m.Gid == semi1.Gid);
        semiMatch1.ParentGid.Should().Be(final.Gid, "semi 1 feeds into final");
    }

    [Fact(DisplayName = "RoundType ordering: S before F")]
    public async Task Brackets_RoundTypeOrdering()
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var field = b.AddField();

        // Final first (higher Gid)
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, rnd: 2, t1Type: "F", t2Type: "F",
            agegroupName: "U10", divName: "");

        // Semi second (lower Gid)
        b.AddGame(job.JobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            null, null, DateTime.Today, rnd: 1, t1Type: "S", t2Type: "S",
            agegroupName: "U10", divName: "");
        await b.SaveAsync();

        var svc = new ViewScheduleService(new ScheduleRepository(ctx), new TeamRepository(ctx));
        var result = await svc.GetBracketsAsync(job.JobId, new ScheduleFilterRequest());

        var matches = result[0].Matches;
        matches[0].RoundType.Should().Be("S", "semis before finals");
        matches[1].RoundType.Should().Be("F", "finals last");
    }
}
