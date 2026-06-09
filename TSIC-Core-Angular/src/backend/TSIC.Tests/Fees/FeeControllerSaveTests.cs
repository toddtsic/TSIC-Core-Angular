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
/// FeeController.SaveFee retroactive-reprice dispatch. The fee math itself lives in the
/// canonical engines (tested elsewhere); these guard the glue that decides WHEN and WHICH
/// engine fires:
///   • a full-payment phase flip ALWAYS reprices (even with RepriceExisting=false),
///   • an amount-only "future only" save does NOT reprice,
///   • Player rows → player engine, ClubRep rows → team engine, each scoped.
/// </summary>
public class FeeControllerSaveTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid RegId = Guid.NewGuid();
    private static readonly Guid AgId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private const string UserId = "director-user";

    private sealed record Harness(
        FeeController Controller,
        Mock<IPlayerRegistrationService> PlayerSvc,
        Mock<ITeamRegistrationService> TeamSvc);

    private static Harness Build(JobFees existing)
    {
        var feeRepo = new Mock<IFeeRepository>();
        feeRepo.Setup(r => r.GetTrackedByScopeAsync(
                JobId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        feeRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var jobLookup = new Mock<IJobLookupService>();
        jobLookup.Setup(j => j.GetJobIdByRegistrationAsync(RegId)).ReturnsAsync(JobId);

        var playerSvc = new Mock<IPlayerRegistrationService>();
        playerSvc.Setup(p => p.RecalculatePlayerFeesAsync(
                JobId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var teamSvc = new Mock<ITeamRegistrationService>();
        teamSvc.Setup(t => t.RecalculateTeamFeesAsync(It.IsAny<RecalculateTeamFeesRequest>(), It.IsAny<string>()))
            .ReturnsAsync(new RecalculateTeamFeesResponse
            {
                UpdatedCount = 2,
                Updates = new(),
                SkippedCount = 0,
                SkippedReasons = new()
            });

        var controller = new FeeController(
            feeRepo.Object, jobLookup.Object, playerSvc.Object, teamSvc.Object,
            new Mock<IAgeGroupRepository>().Object);
        var claims = new[]
        {
            new Claim("regId", RegId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, UserId)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        return new Harness(controller, playerSvc, teamSvc);
    }

    private static JobFees ExistingRow(string roleId, bool? phase) => new()
    {
        JobFeeId = Guid.NewGuid(),
        JobId = JobId,
        RoleId = roleId,
        AgegroupId = AgId,
        TeamId = TeamId,
        BFullPaymentRequired = phase
    };

    [Fact(DisplayName = "Phase flip ALWAYS reprices, even when RepriceExisting is false")]
    public async Task PhaseFlip_ForcesReprice_EvenWhenNotRequested()
    {
        var h = Build(ExistingRow(RoleConstants.Player, phase: null)); // was inherit
        var request = new SaveJobFeeRequest
        {
            RoleId = RoleConstants.Player,
            AgegroupId = AgId,
            TeamId = TeamId,
            BFullPaymentRequired = true,   // null → true = a flip
            RepriceExisting = false        // caller did NOT ask — phase change forces it anyway
        };

        await h.Controller.SaveFee(request, CancellationToken.None);

        h.PlayerSvc.Verify(p => p.RecalculatePlayerFeesAsync(
            JobId, It.IsAny<string>(), AgId, TeamId, It.IsAny<CancellationToken>()), Times.Once);
        h.TeamSvc.Verify(t => t.RecalculateTeamFeesAsync(
            It.IsAny<RecalculateTeamFeesRequest>(), It.IsAny<string>()), Times.Never);
    }

    [Fact(DisplayName = "Amount-only 'future only' save does NOT reprice existing registrations")]
    public async Task AmountOnly_FutureOnly_DoesNotReprice()
    {
        var h = Build(ExistingRow(RoleConstants.Player, phase: true)); // phase unchanged below
        var request = new SaveJobFeeRequest
        {
            RoleId = RoleConstants.Player,
            AgegroupId = AgId,
            TeamId = TeamId,
            Deposit = 99m,                 // amount changed
            BFullPaymentRequired = true,   // SAME as existing → no phase change
            RepriceExisting = false        // future-only
        };

        await h.Controller.SaveFee(request, CancellationToken.None);

        h.PlayerSvc.Verify(p => p.RecalculatePlayerFeesAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "ClubRep 'update all prior' routes to the team engine, scoped to the team")]
    public async Task ClubRep_UpdateAllPrior_RoutesToTeamEngine()
    {
        var h = Build(ExistingRow(RoleConstants.ClubRep, phase: null));
        var request = new SaveJobFeeRequest
        {
            RoleId = RoleConstants.ClubRep,
            AgegroupId = AgId,
            TeamId = TeamId,
            Deposit = 250m,
            RepriceExisting = true         // "update all prior"
        };

        await h.Controller.SaveFee(request, CancellationToken.None);

        h.TeamSvc.Verify(t => t.RecalculateTeamFeesAsync(
            It.Is<RecalculateTeamFeesRequest>(r => r.TeamId == TeamId), It.IsAny<string>()), Times.Once);
        h.PlayerSvc.Verify(p => p.RecalculatePlayerFeesAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
