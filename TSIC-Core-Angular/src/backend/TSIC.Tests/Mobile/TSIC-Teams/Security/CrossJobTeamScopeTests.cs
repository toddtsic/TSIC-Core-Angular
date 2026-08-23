using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TSIC.API.Controllers;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.Tests.Mobile.TSIC_Teams.Security;

/// <summary>
/// teamId arrives off the route on these controllers and nothing in the API scopes by job
/// ambiently, so each action must reject a team belonging to another job itself.
///
/// Covers the five cases the guard has to get right: same job passes, different job is
/// refused, Superuser is exempt, a phase-1 token (no regId) is refused, and an unknown
/// team is refused. The last two matter most -- both are "can't determine", and the guard
/// must fail closed rather than fall through.
/// </summary>
public class CrossJobTeamScopeTests
{
    private static readonly Guid CallerJob = Guid.NewGuid();
    private static readonly Guid OtherJob = Guid.NewGuid();
    private static readonly Guid RegId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();

    private static Mock<IJobLookupService> Lookup(Guid? callerJob, Guid? teamJob)
    {
        var m = new Mock<IJobLookupService>();
        m.Setup(x => x.GetJobIdByRegistrationAsync(RegId)).ReturnsAsync(callerJob);
        m.Setup(x => x.GetJobIdByTeamAsync(TeamId, It.IsAny<CancellationToken>())).ReturnsAsync(teamJob);
        return m;
    }

    private static void Attach(ControllerBase c, bool withRegId = true, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        if (withRegId) claims.Add(new Claim("regId", RegId.ToString()));
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));

        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role))
            }
        };
    }

    private static (TeamManagementController c, Mock<ITeamManagementService> svc) Mgmt(
        Guid? callerJob, Guid? teamJob, bool withRegId = true, string? role = null)
    {
        var svc = new Mock<ITeamManagementService>();
        var c = new TeamManagementController(svc.Object, Lookup(callerJob, teamJob).Object);
        Attach(c, withRegId, role);
        return (c, svc);
    }

    private static void ShouldBe403(IActionResult result) =>
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

    // ── The core five ──

    [Fact(DisplayName = "Same job is allowed through to the service")]
    public async Task SameJob_Allowed()
    {
        var (c, svc) = Mgmt(CallerJob, CallerJob);
        svc.Setup(s => s.GetRosterAsync(TeamId, It.IsAny<CancellationToken>()))
           .ReturnsAsync(new TeamRosterDetailDto { Players = [], Staff = [] });

        var result = await c.GetRoster(TeamId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        svc.Verify(s => s.GetRosterAsync(TeamId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Different job is refused and never reaches the service")]
    public async Task CrossJob_Refused()
    {
        var (c, svc) = Mgmt(CallerJob, OtherJob);

        var result = await c.GetRoster(TeamId, CancellationToken.None);

        ShouldBe403(result);
        svc.Verify(s => s.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Superuser reaches a team in another job")]
    public async Task Superuser_Exempt()
    {
        var (c, svc) = Mgmt(CallerJob, OtherJob, role: RoleConstants.Names.SuperuserName);
        svc.Setup(s => s.GetRosterAsync(TeamId, It.IsAny<CancellationToken>()))
           .ReturnsAsync(new TeamRosterDetailDto { Players = [], Staff = [] });

        var result = await c.GetRoster(TeamId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(DisplayName = "Phase-1 token with no regId fails closed")]
    public async Task NoRegId_Refused()
    {
        var (c, svc) = Mgmt(null, CallerJob, withRegId: false);

        var result = await c.GetRoster(TeamId, CancellationToken.None);

        ShouldBe403(result);
        svc.Verify(s => s.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Unknown team fails closed")]
    public async Task UnknownTeam_Refused()
    {
        var (c, svc) = Mgmt(CallerJob, null);

        var result = await c.GetRoster(TeamId, CancellationToken.None);

        ShouldBe403(result);
        svc.Verify(s => s.GetRosterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Every action on the controller, not just the one above ──

    [Fact(DisplayName = "Every teamId action on TeamManagementController refuses cross-job")]
    public async Task AllMgmtActions_RefuseCrossJob()
    {
        var (c, svc) = Mgmt(CallerJob, OtherJob);
        var ct = CancellationToken.None;

        ShouldBe403(await c.GetRoster(TeamId, ct));
        ShouldBe403(await c.GetLinks(TeamId, ct));
        ShouldBe403(await c.AddLink(TeamId, new AddTeamLinkRequest { Label = "x", DocUrl = "y" }, ct));
        ShouldBe403(await c.DeleteLink(TeamId, Guid.NewGuid(), ct));
        ShouldBe403(await c.GetPushes(TeamId, ct));

        svc.VerifyNoOtherCalls();
    }

    // ── The other two controllers carry the same guard ──

    [Fact(DisplayName = "TeamChatController refuses cross-job")]
    public async Task Chat_RefusesCrossJob()
    {
        var chatRepo = new Mock<IChatRepository>();
        var c = new TeamChatController(chatRepo.Object, Lookup(CallerJob, OtherJob).Object);
        Attach(c);

        var result = await c.GetMessages(
            TeamId, new GetChatMessagesRequest { TeamId = TeamId, PageNumber = 1, RowsPerPage = 20 }, CancellationToken.None);

        ShouldBe403(result);
        chatRepo.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "TeamAttendanceController refuses cross-job on every teamId action")]
    public async Task Attendance_RefusesCrossJob()
    {
        var svc = new Mock<ITeamAttendanceService>();
        var c = new TeamAttendanceController(svc.Object, Lookup(CallerJob, OtherJob).Object);
        Attach(c);
        var ct = CancellationToken.None;

        ShouldBe403(await c.GetEvents(TeamId, ct));
        ShouldBe403(await c.DeleteEvent(TeamId, 1, ct));
        ShouldBe403(await c.GetEventRoster(TeamId, 1, ct));
        ShouldBe403(await c.GetPlayerHistory(TeamId, "u", ct));

        svc.VerifyNoOtherCalls();
    }
}
