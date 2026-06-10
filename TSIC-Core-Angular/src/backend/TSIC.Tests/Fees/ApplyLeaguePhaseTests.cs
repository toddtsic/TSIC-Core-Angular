using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using TSIC.API.Controllers;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Fees;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Fees;

/// <summary>
/// FeeController.ApplyLeaguePhase — the LADT "apply payment phase to all age groups in a
/// league" action. Guards the selection + canonical-reprice glue:
///   • stamps only age groups with a real deposit (phase is meaningless without one),
///   • skips WAITLIST/DROPPED holding buckets,
///   • overwrites a per-age-group phase already set,
///   • reprices ONCE via the canonical engine for the role (player vs team).
/// The fee math itself lives in the engines, tested elsewhere.
/// </summary>
public class ApplyLeaguePhaseTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid RegId = Guid.NewGuid();
    private static readonly Guid LeagueId = Guid.NewGuid();
    private static readonly Guid AgDeposit = Guid.NewGuid();      // has a deposit → stamped (new row)
    private static readonly Guid AgBalanceOnly = Guid.NewGuid();  // balance-only → skipped
    private static readonly Guid AgWaitlist = Guid.NewGuid();     // holding bucket → skipped
    private static readonly Guid AgOverride = Guid.NewGuid();     // has a deposit + own phase → overwritten
    private const string UserId = "director-user";

    private sealed record Harness(
        FeeController Controller,
        Mock<IFeeRepository> FeeRepo,
        Mock<IPlayerRegistrationService> PlayerSvc,
        Mock<ITeamRegistrationService> TeamSvc,
        JobFees OverrideRow);

    private static Harness Build(string roleId)
    {
        var feeRepo = new Mock<IFeeRepository>();

        // Cascade-resolved fees per age group (deposit gates relevance; EffectiveDeposit would
        // mask a balance-only fee, so the endpoint checks the real Deposit).
        feeRepo.Setup(r => r.GetResolvedFeeForAgegroupAsync(JobId, roleId, AgDeposit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { FeeConfigured = true, Deposit = 100m, BalanceDue = 200m });
        feeRepo.Setup(r => r.GetResolvedFeeForAgegroupAsync(JobId, roleId, AgBalanceOnly, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { FeeConfigured = true, Deposit = null, BalanceDue = 150m });
        feeRepo.Setup(r => r.GetResolvedFeeForAgegroupAsync(JobId, roleId, AgOverride, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { FeeConfigured = true, Deposit = 250m, BalanceDue = 500m });

        var overrideRow = new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = JobId,
            RoleId = roleId,
            AgegroupId = AgOverride,
            BFullPaymentRequired = null   // its own phase was OFF → expect overwrite to true
        };
        feeRepo.Setup(r => r.GetTrackedByScopeAsync(JobId, roleId, AgDeposit, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobFees?)null);   // no own row → Add a phase-only row
        feeRepo.Setup(r => r.GetTrackedByScopeAsync(JobId, roleId, AgOverride, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overrideRow);
        feeRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var ageGroups = new Mock<IAgeGroupRepository>();
        ageGroups.Setup(a => a.GetByLeagueIdAsync(LeagueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Agegroups>
            {
                new() { AgegroupId = AgDeposit, AgegroupName = "U10", LeagueId = LeagueId },
                new() { AgegroupId = AgBalanceOnly, AgegroupName = "U12", LeagueId = LeagueId },
                new() { AgegroupId = AgWaitlist, AgegroupName = "WAITLIST U10", LeagueId = LeagueId },
                new() { AgegroupId = AgOverride, AgegroupName = "U14", LeagueId = LeagueId }
            });

        var jobLookup = new Mock<IJobLookupService>();
        jobLookup.Setup(j => j.GetJobIdByRegistrationAsync(RegId)).ReturnsAsync(JobId);

        var playerSvc = new Mock<IPlayerRegistrationService>();
        playerSvc.Setup(p => p.RecalculatePlayerFeesAsync(
                JobId, It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var teamSvc = new Mock<ITeamRegistrationService>();
        teamSvc.Setup(t => t.RecalculateTeamFeesAsync(It.IsAny<RecalculateTeamFeesRequest>(), It.IsAny<string>()))
            .ReturnsAsync(new RecalculateTeamFeesResponse
            {
                UpdatedCount = 4,
                Updates = new(),
                SkippedCount = 0,
                SkippedReasons = new()
            });

        var controller = new FeeController(
            feeRepo.Object, jobLookup.Object, playerSvc.Object, teamSvc.Object, ageGroups.Object);
        var claims = new[]
        {
            new Claim("regId", RegId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, UserId)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        return new Harness(controller, feeRepo, playerSvc, teamSvc, overrideRow);
    }

    [Fact(DisplayName = "Player: stamps only deposit-bearing non-bucket age groups, overwrites existing phase, reprices once")]
    public async Task Player_StampsDepositAgeGroups_OverwritesOverride_RepricesOnce()
    {
        var h = Build(RoleConstants.Player);
        var request = new ApplyLeaguePhaseRequest
        {
            RoleId = RoleConstants.Player,
            BFullPaymentRequired = true
        };

        var result = await h.Controller.ApplyLeaguePhase(LeagueId, request, CancellationToken.None);

        var body = result.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<ApplyLeaguePhaseResponse>().Subject;
        body.AgegroupsApplied.Should().Be(2);          // deposit + override; balance-only & waitlist skipped
        body.RegistrationsRepriced.Should().Be(7);     // canonical player engine result

        // Balance-only and WAITLIST were never stamped.
        h.FeeRepo.Verify(r => r.GetTrackedByScopeAsync(JobId, RoleConstants.Player, AgBalanceOnly, null, null, It.IsAny<CancellationToken>()), Times.Never);
        h.FeeRepo.Verify(r => r.GetResolvedFeeForAgegroupAsync(JobId, RoleConstants.Player, AgWaitlist, It.IsAny<CancellationToken>()), Times.Never);

        // New phase-only row added for the inheriting age group; existing override overwritten.
        h.FeeRepo.Verify(r => r.Add(It.Is<JobFees>(f =>
            f.AgegroupId == AgDeposit && f.BFullPaymentRequired == true && f.Deposit == null && f.BalanceDue == null)), Times.Once);
        h.OverrideRow.BFullPaymentRequired.Should().Be(true);

        // Canonical player engine fired once, whole-job (null scope); team engine untouched.
        h.PlayerSvc.Verify(p => p.RecalculatePlayerFeesAsync(JobId, It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
        h.TeamSvc.Verify(t => t.RecalculateTeamFeesAsync(It.IsAny<RecalculateTeamFeesRequest>(), It.IsAny<string>()), Times.Never);
    }

    [Fact(DisplayName = "ClubRep routes the reprice through the canonical team engine (which rolls club-rep accounts up)")]
    public async Task ClubRep_RepricesViaTeamEngine()
    {
        var h = Build(RoleConstants.ClubRep);
        var request = new ApplyLeaguePhaseRequest
        {
            RoleId = RoleConstants.ClubRep,
            BFullPaymentRequired = true
        };

        var result = await h.Controller.ApplyLeaguePhase(LeagueId, request, CancellationToken.None);

        var body = result.Result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<ApplyLeaguePhaseResponse>().Subject;
        body.AgegroupsApplied.Should().Be(2);
        body.RegistrationsRepriced.Should().Be(4);     // canonical team engine UpdatedCount

        h.TeamSvc.Verify(t => t.RecalculateTeamFeesAsync(
            It.Is<RecalculateTeamFeesRequest>(r => r.JobId == JobId), It.IsAny<string>()), Times.Once);
        h.PlayerSvc.Verify(p => p.RecalculatePlayerFeesAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "An unsupported role is rejected (400) and never reprices")]
    public async Task UnsupportedRole_IsRejected()
    {
        var h = Build(RoleConstants.Player);
        var request = new ApplyLeaguePhaseRequest
        {
            RoleId = Guid.NewGuid().ToString(),   // not Player or ClubRep
            BFullPaymentRequired = true
        };

        var result = await h.Controller.ApplyLeaguePhase(LeagueId, request, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        h.PlayerSvc.Verify(p => p.RecalculatePlayerFeesAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
