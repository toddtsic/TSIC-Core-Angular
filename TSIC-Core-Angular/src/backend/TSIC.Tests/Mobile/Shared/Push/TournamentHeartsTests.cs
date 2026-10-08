using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TSIC.API.Controllers;
using TSIC.API.Services.Shared.Devices;
using TSIC.API.Services.Shared.Firebase;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.JobRules;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Mobile.Shared.Push;

/// <summary>
/// TSIC-Teams hearts on tournament teams (2bcc534aa). Four things have to hold:
///
///  1. The SEND rule: a tournament or league reaches both apps, Events first; every other job
///     type gets exactly what <see cref="PushAudienceResolver.Resolve"/> gives, as one element.
///  2. The heart: a toggle with a registration writes a stamped row (Teams pool); without one it
///     writes the null row (Events pool) exactly as before; a second toggle removes either.
///  3. The score push: a tournament game sends twice, Events then Teams, each with its own pool,
///     and a Teams-side failure cannot undo the Events send that already went out.
///  4. The bearer branch on subscribe-team: a valid non-Scorer stamps; a valid Scorer does not;
///     an UNVALIDATED non-Scorer is 401; an unvalidated Scorer is treated as anonymous, because
///     the Events app's interceptor clears its session on any 401.
/// </summary>
public class TournamentHeartsTests
{
    // ── 1. Send rule ─────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "ResolveSendAudiences: tournament/league → Events then Teams; others unchanged")]
    [InlineData(JobConstants.JobTypeTournament, false, new[] { PushAudience.Events, PushAudience.Teams })]
    [InlineData(JobConstants.JobTypeTournament, true, new[] { PushAudience.Events, PushAudience.Teams })]
    [InlineData(JobConstants.JobTypeLeague, false, new[] { PushAudience.Events, PushAudience.Teams })]
    [InlineData(JobConstants.JobTypeShowcase, true, new PushAudience[0])]
    [InlineData(JobConstants.JobTypeRoot, true, new PushAudience[0])]
    [InlineData(1, true, new[] { PushAudience.Teams })]   // club with the app on
    [InlineData(1, false, new PushAudience[0])]           // club with the app off
    public void SendRule(int jobTypeId, bool teamsEnabled, PushAudience[] expected)
    {
        PushAudienceResolver.ResolveSendAudiences(jobTypeId, teamsEnabled)
            .Should().Equal(expected);
    }

    [Theory(DisplayName = "ResolveSendAudiences never changes what Resolve answers")]
    [InlineData(JobConstants.JobTypeTournament, false)]
    [InlineData(JobConstants.JobTypeLeague, true)]
    [InlineData(JobConstants.JobTypeShowcase, true)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    public void SendRule_PrimaryIsStillResolve(int jobTypeId, bool teamsEnabled)
    {
        var primary = PushAudienceResolver.Resolve(jobTypeId, teamsEnabled);
        var send = PushAudienceResolver.ResolveSendAudiences(jobTypeId, teamsEnabled);

        if (primary == PushAudience.None) send.Should().BeEmpty();
        else send[0].Should().Be(primary, "the first send audience is the one every readout names");
    }

    // ── 2. The heart ─────────────────────────────────────────────────────────────────────

    private static (DeviceManagementService svc, MobileDataBuilder b, Infrastructure.Data.SqlDbContext.SqlDbContext ctx) Heart()
    {
        var ctx = DbContextFactory.Create();
        return (new DeviceManagementService(new DeviceRepository(ctx), new RegistrationRepository(ctx)), new MobileDataBuilder(ctx), ctx);
    }

    [Fact(DisplayName = "Heart with a registration → row stamped (Teams pool); toggle again removes it")]
    public async Task Heart_WithRegistration_StampsThenRemoves()
    {
        var (svc, b, ctx) = Heart();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        b.AddDevice("teams-phone", "ios");
        await b.SaveAsync();
        var regId = Guid.NewGuid();

        var req = new ToggleTeamSubscriptionRequest { DeviceToken = "teams-phone", TeamId = team.TeamId, DeviceType = "ios" };
        var on = await svc.ToggleTeamSubscriptionAsync(req, job.JobId, regId);

        on.SubscribedTeamIds.Should().Contain(team.TeamId);
        var row = await ctx.DeviceTeams.AsNoTracking().SingleAsync(dt => dt.DeviceId == "teams-phone" && dt.TeamId == team.TeamId);
        row.RegistrationId.Should().Be(regId, "a Teams heart carries the login's registration - that is what puts it in the Teams pool");

        var off = await svc.ToggleTeamSubscriptionAsync(req, job.JobId, regId);

        off.SubscribedTeamIds.Should().NotContain(team.TeamId);
        (await ctx.DeviceTeams.AsNoTracking().AnyAsync(dt => dt.DeviceId == "teams-phone")).Should().BeFalse();
    }

    [Fact(DisplayName = "Heart without a registration → null row (Events pool), exactly as before")]
    public async Task Heart_WithoutRegistration_IsAnEventsHeart()
    {
        var (svc, b, ctx) = Heart();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        b.AddDevice("events-phone", "ios");
        await b.SaveAsync();

        await svc.ToggleTeamSubscriptionAsync(
            new ToggleTeamSubscriptionRequest { DeviceToken = "events-phone", TeamId = team.TeamId, DeviceType = "ios" },
            job.JobId);

        var row = await ctx.DeviceTeams.AsNoTracking().SingleAsync(dt => dt.DeviceId == "events-phone");
        row.RegistrationId.Should().BeNull();
    }

    [Fact(DisplayName = "Un-heart removes a row the sync filed (stamped), not only hearts")]
    public async Task UnHeart_RemovesSyncFiledRow()
    {
        var (svc, b, ctx) = Heart();
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var team = b.AddTeam(div.DivId, agegroupId: ag.AgegroupId);
        b.AddDevice("teams-phone", "ios");
        b.AddDeviceTeam("teams-phone", team.TeamId).RegistrationId = Guid.NewGuid();
        await b.SaveAsync();

        var off = await svc.ToggleTeamSubscriptionAsync(
            new ToggleTeamSubscriptionRequest { DeviceToken = "teams-phone", TeamId = team.TeamId, DeviceType = "ios" },
            job.JobId, Guid.NewGuid());

        off.SubscribedTeamIds.Should().NotContain(team.TeamId);
        (await ctx.DeviceTeams.AsNoTracking().AnyAsync(dt => dt.DeviceId == "teams-phone")).Should().BeFalse();
    }

    // ── 3. The score push ────────────────────────────────────────────────────────────────

    private sealed record Send(PushAudience Audience, List<string> Tokens);

    private static (GameResultPushService svc, MobileDataBuilder b, Mock<IScheduleRepository> sched, List<Send> sends, Mock<IFirebasePushService> firebase)
        ScorePush(Func<PushAudience, bool>? throwFor = null)
    {
        var ctx = DbContextFactory.Create();
        var sends = new List<Send>();
        var firebase = new Mock<IFirebasePushService>();
        firebase
            .Setup(f => f.SendToDevicesAsync(
                It.IsAny<PushAudience>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Returns<PushAudience, IReadOnlyList<string>, string, string, string?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (audience, tokens, _, _, _, _, _) =>
                {
                    if (throwFor?.Invoke(audience) == true) throw new InvalidOperationException($"{audience} sender down");
                    sends.Add(new Send(audience, tokens.ToList()));
                    return Task.FromResult(tokens.Count);
                });

        var sched = new Mock<IScheduleRepository>();
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(Environments.Production);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var svc = new GameResultPushService(
            sched.Object, new DeviceRepository(ctx), new PushNotificationRepository(ctx), firebase.Object,
            env.Object, config, NullLogger<GameResultPushService>.Instance);

        return (svc, new MobileDataBuilder(ctx), sched, sends, firebase);
    }

    /// <summary>A tournament with one game; an Events heart on T1 and a Teams heart on T2.</summary>
    private static async Task<(Guid jobId, Guid t1, Guid t2)> TournamentWithBothHearts(
        MobileDataBuilder b, Mock<IScheduleRepository> sched, int gid,
        int jobTypeId = JobConstants.JobTypeTournament, bool teamsEnabled = false)
    {
        var job = b.AddJob();
        job.JobTypeId = jobTypeId;
        job.BEnableTsicteams = teamsEnabled;
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);
        var t1 = b.AddTeam(div.DivId, "Blue", agegroupId: ag.AgegroupId);
        var t2 = b.AddTeam(div.DivId, "Red", agegroupId: ag.AgegroupId);
        b.AddDevice("events-phone", "ios");
        b.AddDevice("teams-phone", "ios");
        b.AddDeviceTeam("events-phone", t1.TeamId);                                   // Events heart
        b.AddDeviceTeam("teams-phone", t2.TeamId).RegistrationId = Guid.NewGuid();    // Teams heart
        await b.SaveAsync();

        sched.Setup(s => s.GetGamePushKeysAsync(gid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GamePushKeysDto
            {
                JobId = job.JobId, T1Id = t1.TeamId, T2Id = t2.TeamId, T1Name = "Blue", T2Name = "Red",
                T1Score = 3, T2Score = 2, AgegroupName = "U10", DivName = "Gold"
            });
        return (job.JobId, t1.TeamId, t2.TeamId);
    }

    [Fact(DisplayName = "Tournament score → two sends: Events pool through Events, then Teams pool through Teams")]
    public async Task Score_Tournament_SendsToBothApps()
    {
        var (svc, b, sched, sends, _) = ScorePush();
        await TournamentWithBothHearts(b, sched, gid: 1);

        await svc.PushGameResultAsync(1);

        sends.Select(s => s.Audience).Should().Equal(PushAudience.Events, PushAudience.Teams);
        sends[0].Tokens.Should().Equal("events-phone");
        sends[1].Tokens.Should().Equal("teams-phone");
    }

    [Fact(DisplayName = "Teams sender failing → Events send already out; nothing thrown to the score save")]
    public async Task Score_TeamsFailure_DoesNotTouchEvents()
    {
        var (svc, b, sched, sends, _) = ScorePush(throwFor: a => a == PushAudience.Teams);
        await TournamentWithBothHearts(b, sched, gid: 2);

        var act = () => svc.PushGameResultAsync(2);

        await act.Should().NotThrowAsync();
        sends.Should().ContainSingle().Which.Audience.Should().Be(PushAudience.Events);
    }

    [Fact(DisplayName = "Club job (app on) score → one send, Teams only; the rule is unchanged off tournaments")]
    public async Task Score_ClubJob_SingleSend()
    {
        var (svc, b, sched, sends, _) = ScorePush();
        await TournamentWithBothHearts(b, sched, gid: 3, jobTypeId: 1, teamsEnabled: true);

        await svc.PushGameResultAsync(3);

        sends.Select(s => s.Audience).Should().Equal(PushAudience.Teams);
    }

    // ── 4. The bearer branch on subscribe-team ───────────────────────────────────────────

    private static (DeviceController c, Mock<IDeviceManagementService> svc) Controller()
    {
        var svc = new Mock<IDeviceManagementService>();
        svc.Setup(s => s.ToggleTeamSubscriptionAsync(
                It.IsAny<ToggleTeamSubscriptionRequest>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToggleTeamSubscriptionResponse { SubscribedTeamIds = [] });
        var c = new DeviceController(svc.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (c, svc);
    }

    private static void Authenticated(ControllerBase c, string role, Guid? regId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1"), new(ClaimTypes.Role, role) };
        if (regId != null) claims.Add(new Claim("regId", regId.Value.ToString()));
        c.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>A JWT the pipeline did NOT validate (the test leaves User anonymous) but whose claims are readable.</summary>
    private static void UnvalidatedBearer(ControllerBase c, string role)
    {
        var jwt = new JwtSecurityToken(claims: [new Claim(ClaimTypes.Role, role), new Claim("regId", Guid.NewGuid().ToString())]);
        c.HttpContext.Request.Headers.Authorization = "Bearer " + new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static Task<IActionResult> Toggle(DeviceController c) =>
        c.ToggleTeamSubscription(new ToggleTeamSubscriptionRequest { DeviceToken = "tok", TeamId = Guid.NewGuid(), DeviceType = "ios" }, Guid.NewGuid(), default);

    private static void ShouldHaveStamped(Mock<IDeviceManagementService> svc, Guid? expected) =>
        svc.Verify(s => s.ToggleTeamSubscriptionAsync(It.IsAny<ToggleTeamSubscriptionRequest>(), It.IsAny<Guid>(), expected, It.IsAny<CancellationToken>()), Times.Once);

    [Fact(DisplayName = "Valid Player bearer → stamped with its registration")]
    public async Task Bearer_ValidPlayer_Stamps()
    {
        var (c, svc) = Controller();
        var regId = Guid.NewGuid();
        Authenticated(c, RoleConstants.Names.PlayerName, regId);

        (await Toggle(c)).Should().BeOfType<OkObjectResult>();
        ShouldHaveStamped(svc, regId);
    }

    [Fact(DisplayName = "Valid Scorer bearer (the Events app) → not stamped, Events heart as before")]
    public async Task Bearer_ValidScorer_DoesNotStamp()
    {
        var (c, svc) = Controller();
        Authenticated(c, RoleConstants.Names.ScorerName, Guid.NewGuid());

        (await Toggle(c)).Should().BeOfType<OkObjectResult>();
        ShouldHaveStamped(svc, null);
    }

    [Fact(DisplayName = "No bearer → not stamped, Events heart as before")]
    public async Task Bearer_None_DoesNotStamp()
    {
        var (c, svc) = Controller();

        (await Toggle(c)).Should().BeOfType<OkObjectResult>();
        ShouldHaveStamped(svc, null);
    }

    [Fact(DisplayName = "Unvalidated (expired) Player bearer → 401, nothing written")]
    public async Task Bearer_ExpiredPlayer_Is401()
    {
        var (c, svc) = Controller();
        UnvalidatedBearer(c, RoleConstants.Names.PlayerName);

        (await Toggle(c)).Should().BeOfType<UnauthorizedResult>();
        svc.Verify(s => s.ToggleTeamSubscriptionAsync(It.IsAny<ToggleTeamSubscriptionRequest>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Unvalidated (expired) Scorer bearer → anonymous path, never a 401 that logs the scorer out")]
    public async Task Bearer_ExpiredScorer_IsAnonymous()
    {
        var (c, svc) = Controller();
        UnvalidatedBearer(c, RoleConstants.Names.ScorerName);

        (await Toggle(c)).Should().BeOfType<OkObjectResult>();
        ShouldHaveStamped(svc, null);
    }

    [Fact(DisplayName = "Garbage Authorization header → anonymous path")]
    public async Task Bearer_Garbage_IsAnonymous()
    {
        var (c, svc) = Controller();
        c.HttpContext.Request.Headers.Authorization = "Bearer not-a-jwt";

        (await Toggle(c)).Should().BeOfType<OkObjectResult>();
        ShouldHaveStamped(svc, null);
    }
}
