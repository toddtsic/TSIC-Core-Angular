using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using TSIC.API.Controllers;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos.Fees;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Fees;

/// <summary>
/// FeeController.GetAffectedCount — the "blast area" read. Guards the scope→agegroup-set
/// mapping (team wins; agegroup is a single id; league EXPANDS to its agegroups; nothing =
/// whole job) and the role dispatch (Player → player count, ClubRep → team count). The
/// counting itself lives in the engines (covered in BlastAreaCountTests); these guard the glue.
/// </summary>
public class FeeControllerAffectedCountTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid RegId = Guid.NewGuid();
    private static readonly Guid AgId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid LeagueId = Guid.NewGuid();
    private static readonly Guid LeagueAgA = Guid.NewGuid();
    private static readonly Guid LeagueAgB = Guid.NewGuid();

    private sealed record Harness(
        FeeController Controller,
        Mock<IPlayerRegistrationService> PlayerSvc,
        Mock<ITeamRegistrationService> TeamSvc,
        Mock<IAgeGroupRepository> AgeGroups);

    private static Harness Build()
    {
        var feeRepo = new Mock<IFeeRepository>();

        var jobLookup = new Mock<IJobLookupService>();
        jobLookup.Setup(j => j.GetJobIdByRegistrationAsync(RegId)).ReturnsAsync(JobId);

        var playerSvc = new Mock<IPlayerRegistrationService>();
        playerSvc.Setup(p => p.CountActivePlayersInScopeAsync(
                JobId, It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var teamSvc = new Mock<ITeamRegistrationService>();
        teamSvc.Setup(t => t.CountEligibleTeamsInScopeAsync(
                JobId, It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(3);

        var ageGroups = new Mock<IAgeGroupRepository>();
        ageGroups.Setup(a => a.GetByLeagueIdAsync(LeagueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Agegroups>
            {
                new() { AgegroupId = LeagueAgA, LeagueId = LeagueId, AgegroupName = "U10" },
                new() { AgegroupId = LeagueAgB, LeagueId = LeagueId, AgegroupName = "U12" },
            });

        var controller = new FeeController(
            feeRepo.Object, jobLookup.Object, playerSvc.Object, teamSvc.Object, ageGroups.Object);
        var claims = new[]
        {
            new Claim("regId", RegId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "director-user")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        return new Harness(controller, playerSvc, teamSvc, ageGroups);
    }

    private static int CountOf(ActionResult<AffectedRegistrationCountDto> result) =>
        ((AffectedRegistrationCountDto)((OkObjectResult)result.Result!).Value!).Count;

    [Fact(DisplayName = "Player + agegroup scope → player count with that single agegroup")]
    public async Task Player_AgegroupScope_PassesSingleAgegroup()
    {
        var h = Build();

        var result = await h.Controller.GetAffectedCount(
            RoleConstants.Player, leagueId: null, agegroupId: AgId, teamId: null, CancellationToken.None);

        CountOf(result).Should().Be(7);
        h.PlayerSvc.Verify(p => p.CountActivePlayersInScopeAsync(
            JobId,
            It.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 1 && ids.Contains(AgId)),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Player + league scope → league expands to its agegroups")]
    public async Task Player_LeagueScope_ExpandsToAgegroups()
    {
        var h = Build();

        await h.Controller.GetAffectedCount(
            RoleConstants.Player, leagueId: LeagueId, agegroupId: null, teamId: null, CancellationToken.None);

        h.AgeGroups.Verify(a => a.GetByLeagueIdAsync(LeagueId, It.IsAny<CancellationToken>()), Times.Once);
        h.PlayerSvc.Verify(p => p.CountActivePlayersInScopeAsync(
            JobId,
            It.Is<IReadOnlyCollection<Guid>?>(ids =>
                ids != null && ids.Count == 2 && ids.Contains(LeagueAgA) && ids.Contains(LeagueAgB)),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Team scope wins → team count with teamId, no agegroup expansion")]
    public async Task ClubRep_TeamScope_RoutesToTeamCount()
    {
        var h = Build();

        var result = await h.Controller.GetAffectedCount(
            RoleConstants.ClubRep, leagueId: null, agegroupId: AgId, teamId: TeamId, CancellationToken.None);

        CountOf(result).Should().Be(3);
        h.TeamSvc.Verify(t => t.CountEligibleTeamsInScopeAsync(
            JobId, null, TeamId), Times.Once);
        // teamId is most specific — league/agegroup are NOT expanded when a team is given
        h.AgeGroups.Verify(a => a.GetByLeagueIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        h.PlayerSvc.Verify(p => p.CountActivePlayersInScopeAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
