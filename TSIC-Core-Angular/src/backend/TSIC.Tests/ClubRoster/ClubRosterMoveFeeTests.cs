using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using TSIC.API.Services.Clubs;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Dtos.ClubRoster;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.ClubRoster;

/// <summary>
/// A club rep moving a player between her own teams must re-resolve the player's fee from the
/// NEW team's scope (team → agegroup → league) — the same canonical applier the admin Roster
/// Swapper uses (FeeResolutionService.ApplySwapFeesAsync). Before this, MovePlayersAsync only
/// re-stamped AssignedTeamId/agegroup and saved, so a cross-agegroup move silently kept the old
/// price. These tests pin both the adapt case and the free-self-rostering no-op.
///
/// Processing is OFF (AccountingDataBuilder.AddJob → BAddProcessingFees false), so FeeBase is the
/// resolved base with no proc gross-up — exact integers.
/// </summary>
public class ClubRosterMoveFeeTests
{
    [Fact(DisplayName = "Club-rep move across agegroups re-resolves the player's fee to the new agegroup")]
    public async Task MoveAcrossAgegroups_RepricesToNewScope()
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);
        var fees = new FeeDataBuilder(ctx);
        var clubRep = Guid.NewGuid();

        var job = acct.AddJob();
        var league = acct.AddLeague(job.JobId);
        var agA = acct.AddAgegroup(league.LeagueId, "U12");
        var agB = acct.AddAgegroup(league.LeagueId, "U14");
        var teamA = acct.AddTeam(job.JobId, agA.AgegroupId, clubRepRegistrationId: clubRep, teamName: "A");
        var teamB = acct.AddTeam(job.JobId, agB.AgegroupId, clubRepRegistrationId: clubRep, teamName: "B");

        // Different player fee per agegroup (balance-only base → FeeBase == BalanceDue).
        fees.AddJobFee(job.JobId, RoleConstants.Player, agegroupId: agA.AgegroupId, balanceDue: 100m);
        fees.AddJobFee(job.JobId, RoleConstants.Player, agegroupId: agB.AgegroupId, balanceDue: 250m);

        var reg = fees.AddRegistration(job.JobId, teamA.TeamId, feeBase: 100m);
        await acct.SaveAsync();

        var svc = BuildService(ctx);
        await svc.MovePlayersAsync(
            new MovePlayersRequest { TargetTeamId = teamB.TeamId, RegistrationIds = new List<Guid> { reg.RegistrationId } },
            clubRep, job.JobId);

        reg.AssignedTeamId.Should().Be(teamB.TeamId, "the move re-assigns the player");
        reg.FeeBase.Should().Be(250m, "the fee must adapt to the U14 agegroup's price, not keep U12's");
    }

    [Fact(DisplayName = "Free self-rostering move stays $0 (no fee configured on either team)")]
    public async Task MoveBetweenFreeTeams_StaysZero()
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);
        var fees = new FeeDataBuilder(ctx);
        var clubRep = Guid.NewGuid();

        var job = acct.AddJob();
        var league = acct.AddLeague(job.JobId);
        var agA = acct.AddAgegroup(league.LeagueId, "U12");
        var agB = acct.AddAgegroup(league.LeagueId, "U14");
        var teamA = acct.AddTeam(job.JobId, agA.AgegroupId, clubRepRegistrationId: clubRep, teamName: "A");
        var teamB = acct.AddTeam(job.JobId, agB.AgegroupId, clubRepRegistrationId: clubRep, teamName: "B");

        // No JobFees rows → free event. Applier is no-throw on an unconfigured fee → resolves $0.
        var reg = fees.AddRegistration(job.JobId, teamA.TeamId, feeBase: 0m);
        await acct.SaveAsync();

        var svc = BuildService(ctx);
        await svc.MovePlayersAsync(
            new MovePlayersRequest { TargetTeamId = teamB.TeamId, RegistrationIds = new List<Guid> { reg.RegistrationId } },
            clubRep, job.JobId);

        reg.AssignedTeamId.Should().Be(teamB.TeamId);
        reg.FeeBase.Should().Be(0m, "a free → free self-roster move must not invent a fee");
        reg.OwedTotal.Should().Be(0m);
    }

    private static ClubRosterService BuildService(SqlDbContext ctx)
    {
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);   // present but inert — processing is off
        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object);
        var feeService = new FeeResolutionService(new FeeRepository(ctx), jobRepo.Object, paymentState);
        return new ClubRosterService(
            new TeamRepository(ctx), new RegistrationRepository(ctx), feeService, jobRepo.Object);
    }
}
