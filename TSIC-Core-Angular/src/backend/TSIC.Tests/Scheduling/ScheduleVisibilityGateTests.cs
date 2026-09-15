using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TSIC.API.Controllers;
using TSIC.API.Extensions;
using TSIC.API.Services.Invites;
using TSIC.API.Services.Scheduling;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Scheduling;

/// <summary>
/// UNRELEASED-SCHEDULE GATE
///
/// Before the gate, api/view-schedule served any job's schedule to anyone holding a jobPath, gid or
/// teamId. The rule (ViewScheduleService.CanViewScheduleAsync): public schedule → everyone; otherwise only
/// a caller logged in to THAT job as Superuser/Director/SuperDirector/Scorer, or as a Club Rep holding a valid
/// schedule-preview invite while the preview door is open. Every other caller gets the generic "Schedule not
/// available" 404, identical to the response for an event, game or team that doesn't exist.
///
/// Part 1 pins the rule; Part 1b the preview invite. Part 2 drives the view-schedule controller, proving every data endpoint applies
/// it — to the job that OWNS a gid/teamId, and to the jobPath's job rather than the caller's login job.
/// Part 3 covers the other schedule-derived surfaces: active-games, the filter tree and the pulse.
/// </summary>
public class ScheduleVisibilityGateTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Fixture
    // ═══════════════════════════════════════════════════════════════════

    private sealed class Fixture
    {
        public required SqlDbContext Ctx { get; init; }
        public required ViewScheduleService Svc { get; init; }
        public required Jobs PublicJob { get; init; }
        public required Jobs DraftJob { get; init; }
        public required int PublicGid { get; init; }
        public required int DraftGid { get; init; }
        public required Guid PublicTeamId { get; init; }
        public required Guid DraftTeamId { get; init; }
        /// <summary>An active Club Rep registered on the unreleased job — the one a preview invite goes to.</summary>
        public required Registrations RepReg { get; init; }
    }

    private static async Task<Fixture> BuildAsync(bool draftPreviewFlag = false)
    {
        var ctx = DbContextFactory.Create();
        var b = new MobileDataBuilder(ctx);

        var publicJob = b.AddJob("Public Cup", "public-cup", publicAccess: true);
        var draftJob = b.AddJob("Draft Cup", "draft-cup", publicAccess: false);
        draftJob.BAllowClubRepSchedulePreview = draftPreviewFlag;

        var (publicGid, publicTeam) = SeedGame(b, publicJob.JobId);
        var (draftGid, draftTeam) = SeedGame(b, draftJob.JobId);
        var rep = b.AddUser("rep1");
        var repReg = b.AddRegistration(rep.Id, draftJob.JobId, RoleConstants.ClubRep);
        await b.SaveAsync();

        return new Fixture
        {
            Ctx = ctx,
            Svc = SchedulingTestFactory.ViewSchedule(ctx),
            PublicJob = publicJob,
            DraftJob = draftJob,
            PublicGid = publicGid,
            DraftGid = draftGid,
            PublicTeamId = publicTeam,
            DraftTeamId = draftTeam,
            RepReg = repReg
        };
    }

    private static (int gid, Guid teamId) SeedGame(MobileDataBuilder b, Guid jobId)
    {
        var league = b.AddLeague(jobId);
        var ag = b.AddAgegroup(league.LeagueId, "U10");
        var div = b.AddDivision(ag.AgegroupId, "Gold");
        var t1 = b.AddTeam(div.DivId, "Eagles", ag.AgegroupId, jobId, league.LeagueId);
        var t2 = b.AddTeam(div.DivId, "Hawks", ag.AgegroupId, jobId, league.LeagueId);
        var field = b.AddField("Field 1", "Allentown", "PA");
        var game = b.AddGame(jobId, league.LeagueId, field.FieldId, ag.AgegroupId, div.DivId,
            t1.TeamId, t2.TeamId, DateTime.Today, agegroupName: "U10", divName: "Gold", t1Name: "Eagles", t2Name: "Hawks");
        return (game.Gid, t1.TeamId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Part 1 — the rule
    // ═══════════════════════════════════════════════════════════════════

    private static ScheduleViewer LoggedIn(Guid jobId, string role) =>
        new() { JobId = jobId, RegistrationId = Guid.NewGuid(), UserId = "user-1", Role = role };

    [Fact(DisplayName = "Public schedule: an anonymous caller may view")]
    public async Task Public_Anonymous_Allowed()
    {
        var f = await BuildAsync();
        (await f.Svc.CanViewScheduleAsync(f.PublicJob.JobId, ScheduleViewer.Anonymous)).Should().BeTrue();
    }

    [Fact(DisplayName = "Public schedule: a caller logged in to a different job may view")]
    public async Task Public_OtherJobCaller_Allowed()
    {
        var f = await BuildAsync();
        (await f.Svc.CanViewScheduleAsync(f.PublicJob.JobId, LoggedIn(f.DraftJob.JobId, RoleConstants.Names.PlayerName)))
            .Should().BeTrue();
    }

    [Fact(DisplayName = "Unreleased schedule: an anonymous caller is refused")]
    public async Task Draft_Anonymous_Refused()
    {
        var f = await BuildAsync();
        (await f.Svc.CanViewScheduleAsync(f.DraftJob.JobId, ScheduleViewer.Anonymous)).Should().BeFalse();
    }

    [Theory(DisplayName = "Unreleased schedule: event-runner roles logged in to THAT job may view")]
    [InlineData(RoleConstants.Names.SuperuserName)]
    [InlineData(RoleConstants.Names.DirectorName)]
    [InlineData(RoleConstants.Names.SuperDirectorName)]
    [InlineData(RoleConstants.Names.ScorerName)]
    public async Task Draft_EventRunner_SameJob_Allowed(string role)
    {
        var f = await BuildAsync();
        (await f.Svc.CanViewScheduleAsync(f.DraftJob.JobId, LoggedIn(f.DraftJob.JobId, role))).Should().BeTrue();
    }

    [Theory(DisplayName = "Unreleased schedule: event-runner roles of a DIFFERENT job are refused")]
    [InlineData(RoleConstants.Names.SuperuserName)]
    [InlineData(RoleConstants.Names.DirectorName)]
    [InlineData(RoleConstants.Names.ScorerName)]
    public async Task Draft_EventRunner_OtherJob_Refused(string role)
    {
        var f = await BuildAsync();
        (await f.Svc.CanViewScheduleAsync(f.DraftJob.JobId, LoggedIn(f.PublicJob.JobId, role))).Should().BeFalse();
    }

    [Theory(DisplayName = "Unreleased schedule: registrant roles of that job without a preview invite are refused — Club Rep included, preview flag or not")]
    [InlineData(RoleConstants.Names.ClubRepName, false)]
    [InlineData(RoleConstants.Names.ClubRepName, true)]
    [InlineData(RoleConstants.Names.PlayerName, false)]
    [InlineData(RoleConstants.Names.PlayerName, true)]
    [InlineData(RoleConstants.Names.StaffName, true)]
    [InlineData(RoleConstants.Names.FamilyName, true)]
    [InlineData(RoleConstants.Names.RefereeName, true)]
    [InlineData(RoleConstants.Names.StoreAdminName, true)]
    public async Task Draft_RegistrantRoles_Refused(string role, bool previewFlag)
    {
        var f = await BuildAsync(draftPreviewFlag: previewFlag);
        (await f.Svc.CanViewScheduleAsync(f.DraftJob.JobId, LoggedIn(f.DraftJob.JobId, role))).Should().BeFalse();
    }

    [Fact(DisplayName = "NULL BScheduleAllowPublicAccess reads as unreleased")]
    public async Task NullPublicFlag_TreatedAsUnreleased()
    {
        var f = await BuildAsync();
        f.DraftJob.BScheduleAllowPublicAccess = null;
        await f.Ctx.SaveChangesAsync();

        (await f.Svc.CanViewScheduleAsync(f.DraftJob.JobId, ScheduleViewer.Anonymous)).Should().BeFalse();
    }

    [Fact(DisplayName = "Unknown job is refused")]
    public async Task UnknownJob_Refused()
    {
        var f = await BuildAsync();
        (await f.Svc.CanViewScheduleAsync(Guid.NewGuid(), ScheduleViewer.Anonymous)).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Part 1b — the Club Rep schedule-preview invite
    // ═══════════════════════════════════════════════════════════════════

    private static string PreviewToken(Fixture f, InvitePurpose purpose = InvitePurpose.SchedulePreview,
        Guid? jobId = null, string? userId = null, DateTime? expires = null) =>
        TestInviteTokens.Build().Create(purpose, jobId ?? f.DraftJob.JobId, userId ?? f.RepReg.UserId!,
            expires ?? DateTime.Now.AddHours(24));

    /// <summary>The invited rep, logged in to the unreleased job, presenting <paramref name="token"/>.</summary>
    private static ScheduleViewer Rep(Fixture f, string? token) => new()
    {
        JobId = f.DraftJob.JobId,
        RegistrationId = f.RepReg.RegistrationId,
        UserId = f.RepReg.UserId,
        Role = RoleConstants.Names.ClubRepName,
        PreviewToken = token
    };

    private static Task<bool> CanViewDraft(Fixture f, ScheduleViewer viewer) =>
        f.Svc.CanViewScheduleAsync(f.DraftJob.JobId, viewer);

    [Fact(DisplayName = "Preview: the invited Club Rep with a valid token, flag on, may view the unreleased schedule")]
    public async Task Preview_ValidInvite_Allowed()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f)))).Should().BeTrue();
    }

    [Fact(DisplayName = "Preview: no token → refused")]
    public async Task Preview_NoToken_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, null))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: a registration invite token is not a preview invite → refused")]
    public async Task Preview_RegistrationPurposeToken_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f, purpose: InvitePurpose.Registration)))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: a token minted for another user, logged in as the rep → refused (invite user = login user)")]
    public async Task Preview_OtherUsersToken_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f, userId: "someone-else")))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: a token minted for another job → refused")]
    public async Task Preview_OtherJobsToken_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f, jobId: f.PublicJob.JobId)))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: an expired token → refused")]
    public async Task Preview_ExpiredToken_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f, expires: DateTime.Now.AddMinutes(-10))))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: logged in to another job with a valid token → refused (login event = invite event)")]
    public async Task Preview_LoggedInElsewhere_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f)) with { JobId = f.PublicJob.JobId })).Should().BeFalse();
    }

    [Theory(DisplayName = "Preview: the rep's user logged in under a non-Club-Rep role with a valid token → refused")]
    [InlineData(RoleConstants.Names.PlayerName)]
    [InlineData(RoleConstants.Names.StaffName)]
    [InlineData(RoleConstants.Names.FamilyName)]
    public async Task Preview_WrongRole_Refused(string role)
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f)) with { Role = role })).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: a valid token presented with a DIFFERENT registration of the same user → refused")]
    public async Task Preview_OtherRegistration_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        (await CanViewDraft(f, Rep(f, PreviewToken(f)) with { RegistrationId = Guid.NewGuid() })).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview kill switch: flag turned off after the invite went out → refused")]
    public async Task Preview_FlagOff_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: false);
        (await CanViewDraft(f, Rep(f, PreviewToken(f)))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: the rep deactivated after the invite went out → refused")]
    public async Task Preview_InactiveRep_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        f.RepReg.BActive = false;
        await f.Ctx.SaveChangesAsync();

        (await CanViewDraft(f, Rep(f, PreviewToken(f)))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: the event expired for users after the invite went out → refused")]
    public async Task Preview_EventExpired_Refused()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        f.DraftJob.ExpiryUsers = DateTime.Now.AddDays(-1);
        await f.Ctx.SaveChangesAsync();

        (await CanViewDraft(f, Rep(f, PreviewToken(f)))).Should().BeFalse();
    }

    [Fact(DisplayName = "Preview: once released, the rep sees it with or without a token, like everyone")]
    public async Task Preview_Released_EveryoneSees()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        f.DraftJob.BScheduleAllowPublicAccess = true;
        await f.Ctx.SaveChangesAsync();

        (await CanViewDraft(f, Rep(f, null))).Should().BeTrue();
        (await CanViewDraft(f, ScheduleViewer.Anonymous)).Should().BeTrue();
    }

    [Fact(DisplayName = "Preview: a token does nothing for a Director — they see the unreleased schedule regardless")]
    public async Task Preview_DirectorUnaffected()
    {
        var f = await BuildAsync(draftPreviewFlag: false);
        (await CanViewDraft(f, LoggedIn(f.DraftJob.JobId, RoleConstants.Names.DirectorName))).Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Part 2 — the controller applies it on every data endpoint
    // ═══════════════════════════════════════════════════════════════════

    private static ViewScheduleController Controller(Fixture f, Guid? loggedInJobId = null, string? role = null)
    {
        var (lookup, context) = Caller(f, loggedInJobId, role);
        return new ViewScheduleController(f.Svc, lookup) { ControllerContext = context };
    }

    /// <summary>
    /// A job lookup over the fixture plus a request context for an anonymous or logged-in caller. <paramref name="asRep"/>
    /// logs in as the fixture's Club Rep registration instead of a fresh one; <paramref name="previewToken"/> rides
    /// the preview header.
    /// </summary>
    private static (IJobLookupService lookup, ControllerContext context) Caller(
        Fixture f, Guid? loggedInJobId = null, string? role = null, bool asRep = false, string? previewToken = null)
    {
        var lookup = new Mock<IJobLookupService>();
        lookup.Setup(l => l.GetJobIdByPathAsync(f.PublicJob.JobPath)).ReturnsAsync(f.PublicJob.JobId);
        lookup.Setup(l => l.GetJobIdByPathAsync(f.DraftJob.JobPath)).ReturnsAsync(f.DraftJob.JobId);
        lookup.Setup(l => l.GetJobIdByTeamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid teamId, CancellationToken _) =>
                Task.FromResult(f.Ctx.Teams.Where(t => t.TeamId == teamId).Select(t => (Guid?)t.JobId).FirstOrDefault()));

        var identity = new ClaimsIdentity();
        if (loggedInJobId.HasValue)
        {
            var regId = asRep ? f.RepReg.RegistrationId : Guid.NewGuid();
            lookup.Setup(l => l.GetJobIdByRegistrationAsync(regId)).ReturnsAsync(loggedInJobId.Value);
            identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, asRep ? f.RepReg.UserId! : "user-1"),
                new Claim("regId", regId.ToString()),
                new Claim(ClaimTypes.Role, role ?? RoleConstants.Names.PlayerName)
            ], authenticationType: "Test");
        }

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        if (previewToken != null)
            http.Request.Headers[ScheduleVisibilityExtensions.SchedulePreviewHeader] = previewToken;
        return (lookup.Object, new ControllerContext { HttpContext = http });
    }

    /// <summary>The one refusal: 404, "Schedule not available". No 403, nothing that says a schedule exists.</summary>
    private static void ShouldBeUnavailable(IActionResult? result)
    {
        var obj = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        obj.Value.Should().BeOfType<ProblemDetails>().Which.Detail.Should().Be("Schedule not available");
    }

    private static void ShouldBeOk(IActionResult? result) =>
        result.Should().BeOfType<OkObjectResult>();

    [Fact(DisplayName = "Anonymous + unreleased jobPath: filter-options, games, standings, team-records, brackets all not available")]
    public async Task Anonymous_DraftJobPath_AllTabEndpointsForbidden()
    {
        var f = await BuildAsync();
        var c = Controller(f);
        var path = f.DraftJob.JobPath;
        var req = new ScheduleFilterRequest();

        ShouldBeUnavailable((await c.GetFilterOptions(path, default)).Result);
        ShouldBeUnavailable((await c.GetGames(req, path, default)).Result);
        ShouldBeUnavailable((await c.GetStandings(req, path, default)).Result);
        ShouldBeUnavailable((await c.GetTeamRecords(req, path, default)).Result);
        ShouldBeUnavailable((await c.GetBrackets(req, path, default)).Result);
    }

    [Fact(DisplayName = "Anonymous + public jobPath: games and standings are served")]
    public async Task Anonymous_PublicJobPath_Served()
    {
        var f = await BuildAsync();
        var c = Controller(f);

        ShouldBeOk((await c.GetGames(new ScheduleFilterRequest(), f.PublicJob.JobPath, default)).Result);
        ShouldBeOk((await c.GetStandings(new ScheduleFilterRequest(), f.PublicJob.JobPath, default)).Result);
    }

    [Fact(DisplayName = "Capabilities stays open and reports canView=false for an anonymous caller on an unreleased job")]
    public async Task Capabilities_Draft_ReportsCannotView()
    {
        var f = await BuildAsync();
        var result = (await Controller(f).GetCapabilities(f.DraftJob.JobPath, default)).Result;

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ScheduleCapabilitiesDto>()
            .Which.CanView.Should().BeFalse();
    }

    [Fact(DisplayName = "Capabilities reports canView=true for a Director of the unreleased job")]
    public async Task Capabilities_Draft_DirectorCanView()
    {
        var f = await BuildAsync();
        var c = Controller(f, loggedInJobId: f.DraftJob.JobId, role: RoleConstants.Names.DirectorName);
        var result = (await c.GetCapabilities(null, default)).Result;

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ScheduleCapabilitiesDto>()
            .Which.CanView.Should().BeTrue();
    }

    [Fact(DisplayName = "Director of the unreleased job: games are served")]
    public async Task Director_DraftJob_GamesServed()
    {
        var f = await BuildAsync();
        var c = Controller(f, loggedInJobId: f.DraftJob.JobId, role: RoleConstants.Names.DirectorName);

        ShouldBeOk((await c.GetGames(new ScheduleFilterRequest(), null, default)).Result);
    }

    [Fact(DisplayName = "Player of the unreleased job: games and contacts not available")]
    public async Task Player_DraftJob_Forbidden()
    {
        var f = await BuildAsync();
        var c = Controller(f, loggedInJobId: f.DraftJob.JobId, role: RoleConstants.Names.PlayerName);

        ShouldBeUnavailable((await c.GetGames(new ScheduleFilterRequest(), null, default)).Result);
        ShouldBeUnavailable((await c.GetContacts(new ScheduleFilterRequest(), default)).Result);
    }

    [Fact(DisplayName = "Deep links by gid (sequential, enumerable): unreleased job's game → not available; public job's → served")]
    public async Task ByGame_GatesOnOwningJob()
    {
        var f = await BuildAsync();
        var c = Controller(f);

        ShouldBeUnavailable((await c.GetBracketsByGame(f.DraftGid, default)).Result);
        ShouldBeUnavailable((await c.GetStandingsByGame(f.DraftGid, default)).Result);
        ShouldBeOk((await c.GetStandingsByGame(f.PublicGid, default)).Result);
    }

    [Fact(DisplayName = "Deep links by gid: an unknown gid is 404")]
    public async Task ByGame_UnknownGid_NotFound()
    {
        var f = await BuildAsync();
        ShouldBeUnavailable((await Controller(f).GetStandingsByGame(999_999, default)).Result);
    }

    [Fact(DisplayName = "Hidden and nonexistent are indistinguishable: unreleased vs unknown gid, teamId and jobPath give the same response")]
    public async Task Unreleased_And_Unknown_Identical()
    {
        var f = await BuildAsync();
        var c = Controller(f);

        static string Shape(IActionResult? r)
        {
            var o = (ObjectResult)r!;
            var p = (ProblemDetails)o.Value!;
            return $"{o.StatusCode}|{p.Status}|{p.Title}|{p.Detail}|{p.Type}";
        }

        Shape((await c.GetStandingsByGame(f.DraftGid, default)).Result)
            .Should().Be(Shape((await c.GetStandingsByGame(999_999, default)).Result));
        Shape((await c.GetStandingsByTeam(f.DraftTeamId, default)).Result)
            .Should().Be(Shape((await c.GetStandingsByTeam(Guid.NewGuid(), default)).Result));
        Shape((await c.GetGames(new ScheduleFilterRequest(), f.DraftJob.JobPath, default)).Result)
            .Should().Be(Shape((await c.GetGames(new ScheduleFilterRequest(), "no-such-event", default)).Result));
    }

    [Fact(DisplayName = "Deep links by teamId: unreleased job's team → not available for brackets and standings")]
    public async Task ByTeam_GatesOnOwningJob()
    {
        var f = await BuildAsync();
        var c = Controller(f);

        ShouldBeUnavailable((await c.GetBracketsByTeam(f.DraftTeamId, default)).Result);
        ShouldBeUnavailable((await c.GetStandingsByTeam(f.DraftTeamId, default)).Result);
        ShouldBeOk((await c.GetStandingsByTeam(f.PublicTeamId, default)).Result);
    }

    [Fact(DisplayName = "Team results: a public jobPath cannot be used to read an unreleased job's team")]
    public async Task TeamResults_PublicJobPath_DraftTeam_Forbidden()
    {
        var f = await BuildAsync();
        var c = Controller(f);

        ShouldBeUnavailable((await c.GetTeamResults(f.DraftTeamId, f.PublicJob.JobPath, default)).Result);
        ShouldBeOk((await c.GetTeamResults(f.PublicTeamId, f.PublicJob.JobPath, default)).Result);
    }

    [Fact(DisplayName = "Director of job A cannot reach job B's unreleased game through a gid deep link")]
    public async Task ByGame_DirectorOfOtherJob_Forbidden()
    {
        var f = await BuildAsync();
        var c = Controller(f, loggedInJobId: f.PublicJob.JobId, role: RoleConstants.Names.DirectorName);

        ShouldBeUnavailable((await c.GetStandingsByGame(f.DraftGid, default)).Result);
    }

    [Fact(DisplayName = "Invited Club Rep through the controller: the preview header opens games and capabilities; without it they refuse")]
    public async Task ClubRep_PreviewHeader_ThroughController()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        var path = f.DraftJob.JobPath;
        var (lookup, withHeader) = Caller(f, f.DraftJob.JobId, RoleConstants.Names.ClubRepName, asRep: true, previewToken: PreviewToken(f));
        var (lookup2, noHeader) = Caller(f, f.DraftJob.JobId, RoleConstants.Names.ClubRepName, asRep: true);
        var invited = new ViewScheduleController(f.Svc, lookup) { ControllerContext = withHeader };
        var uninvited = new ViewScheduleController(f.Svc, lookup2) { ControllerContext = noHeader };

        ShouldBeOk((await invited.GetGames(new ScheduleFilterRequest(), path, default)).Result);
        ((OkObjectResult)(await invited.GetCapabilities(path, default)).Result!).Value
            .Should().BeOfType<ScheduleCapabilitiesDto>().Which.CanView.Should().BeTrue();
        ShouldBeUnavailable((await uninvited.GetGames(new ScheduleFilterRequest(), path, default)).Result);
    }

    [Fact(DisplayName = "Club Rep of the unreleased job with the preview flag ON but no invite: games and capabilities refuse")]
    public async Task ClubRep_DraftJob_FlagOn_Forbidden()
    {
        var f = await BuildAsync(draftPreviewFlag: true);
        var c = Controller(f, loggedInJobId: f.DraftJob.JobId, role: RoleConstants.Names.ClubRepName);

        ShouldBeUnavailable((await c.GetGames(new ScheduleFilterRequest(), f.DraftJob.JobPath, default)).Result);
        (await c.GetCapabilities(f.DraftJob.JobPath, default)).Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<ScheduleCapabilitiesDto>().Which.CanView.Should().BeFalse();
    }

    [Fact(DisplayName = "jobPath wins over the login: a Director of the unreleased job browsing the PUBLIC job's page gets the public job's games")]
    public async Task JobPath_WinsOverLogin_ServesPagesJob()
    {
        var f = await BuildAsync();
        var c = Controller(f, loggedInJobId: f.DraftJob.JobId, role: RoleConstants.Names.DirectorName);

        var games = (await c.GetGames(new ScheduleFilterRequest(), f.PublicJob.JobPath, default)).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeAssignableTo<IEnumerable<ViewGameDto>>().Subject.ToList();

        games.Should().ContainSingle().Which.Gid.Should().Be(f.PublicGid,
            "the page is the public job's; serving the login's job put one event's schedule on another's page");
    }

    [Fact(DisplayName = "jobPath wins over the login: a Director of the PUBLIC job cannot read the unreleased job by its jobPath")]
    public async Task JobPath_OtherJobsDirector_Forbidden()
    {
        var f = await BuildAsync();
        var c = Controller(f, loggedInJobId: f.PublicJob.JobId, role: RoleConstants.Names.DirectorName);

        ShouldBeUnavailable((await c.GetGames(new ScheduleFilterRequest(), f.DraftJob.JobPath, default)).Result);
    }

    [Fact(DisplayName = "Admin rights are per-job: a Director logged in elsewhere is not an admin on this job's page")]
    public async Task Capabilities_DirectorOfOtherJob_NotAdmin()
    {
        var f = await BuildAsync();
        var elsewhere = Controller(f, loggedInJobId: f.DraftJob.JobId, role: RoleConstants.Names.DirectorName);
        var here = Controller(f, loggedInJobId: f.PublicJob.JobId, role: RoleConstants.Names.DirectorName);

        var elsewhereCaps = ((OkObjectResult)(await elsewhere.GetCapabilities(f.PublicJob.JobPath, default)).Result!).Value as ScheduleCapabilitiesDto;
        var hereCaps = ((OkObjectResult)(await here.GetCapabilities(f.PublicJob.JobPath, default)).Result!).Value as ScheduleCapabilitiesDto;

        elsewhereCaps!.CanScore.Should().BeFalse();
        hereCaps!.CanScore.Should().BeTrue("the control: the same Director on their own job is an admin");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Part 3 — the other schedule-derived surfaces
    // ═══════════════════════════════════════════════════════════════════

    private static EventBrowseController EventBrowse(Fixture f, Guid? loggedInJobId = null, string? role = null)
    {
        var (lookup, context) = Caller(f, loggedInJobId, role);
        var eventBrowse = new Mock<IEventBrowseService>();
        eventBrowse.Setup(e => e.GetActiveGamesAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameClockAvailableGameTimesDto { AvailableRRGameData = [], AvailablePOGameData = [] });
        return new EventBrowseController(eventBrowse.Object, lookup, f.Svc) { ControllerContext = context };
    }

    [Fact(DisplayName = "active-games: unreleased job → not available anonymous; served to its Director and for a public job")]
    public async Task ActiveGames_Gated()
    {
        var f = await BuildAsync();

        ShouldBeUnavailable(await EventBrowse(f).GetActiveGames(f.DraftJob.JobId, null, default));
        ShouldBeUnavailable(await EventBrowse(f, f.DraftJob.JobId, RoleConstants.Names.ClubRepName).GetActiveGames(f.DraftJob.JobId, null, default));
        (await EventBrowse(f, f.DraftJob.JobId, RoleConstants.Names.DirectorName).GetActiveGames(f.DraftJob.JobId, null, default))
            .Should().BeOfType<OkObjectResult>();
        (await EventBrowse(f).GetActiveGames(f.PublicJob.JobId, null, default)).Should().BeOfType<OkObjectResult>();
    }

    private static JobFilterTreeController FilterTree(Fixture f, Guid? loggedInJobId = null, string? role = null)
    {
        var (lookup, context) = Caller(f, loggedInJobId, role);
        return new JobFilterTreeController(new JobFilterTreeRepository(f.Ctx), lookup, f.Svc) { ControllerContext = context };
    }

    private static List<bool> ScheduledFlags(JobFilterTreeDto tree) =>
    [
        .. tree.Cadt.SelectMany(c => c.Agegroups).SelectMany(a => a.Divisions).SelectMany(d => d.Teams).Select(t => t.IsScheduled),
        .. tree.Ladt.SelectMany(a => a.Divisions).SelectMany(d => d.Teams).Select(t => t.IsScheduled)
    ];

    private static async Task<JobFilterTreeDto> TreeAsync(JobFilterTreeController c, string? jobPath) =>
        (JobFilterTreeDto)((OkObjectResult)(await c.Get(jobPath, default)).Result!).Value!;

    [Fact(DisplayName = "Filter tree: the unreleased job's IsScheduled flags read false for anonymous and Club Rep")]
    public async Task FilterTree_Draft_ScheduleFlagsHidden()
    {
        var f = await BuildAsync(draftPreviewFlag: true);

        var anon = ScheduledFlags(await TreeAsync(FilterTree(f), f.DraftJob.JobPath));
        var rep = ScheduledFlags(await TreeAsync(FilterTree(f, f.DraftJob.JobId, RoleConstants.Names.ClubRepName), null));

        anon.Should().NotBeEmpty("the tree itself (teams) is not schedule data and is still served");
        anon.Should().OnlyContain(s => !s);
        rep.Should().NotBeEmpty().And.OnlyContain(s => !s);
    }

    [Fact(DisplayName = "Filter tree: IsScheduled is real for the unreleased job's Director and for a public job")]
    public async Task FilterTree_ScheduleFlagsShownWhenViewable()
    {
        var f = await BuildAsync();

        ScheduledFlags(await TreeAsync(FilterTree(f, f.DraftJob.JobId, RoleConstants.Names.DirectorName), null))
            .Should().Contain(true, "the Director's rescheduler filters on it");
        ScheduledFlags(await TreeAsync(FilterTree(f), f.PublicJob.JobPath)).Should().Contain(true);
    }

    [Fact(DisplayName = "Pulse: first/last game dates are null while the schedule is unreleased, present once released")]
    public async Task Pulse_GameDates_OnlyWhenReleased()
    {
        var f = await BuildAsync();
        var repo = new JobRepository(f.Ctx);

        var draft = await repo.GetJobPulseAsync(f.DraftJob.JobPath);
        var released = await repo.GetJobPulseAsync(f.PublicJob.JobPath);

        draft!.FirstGameDate.Should().BeNull();
        draft.LastGameDate.Should().BeNull();
        released!.FirstGameDate.Should().Be(DateTime.Today);
        released.LastGameDate.Should().Be(DateTime.Today);
    }
}
