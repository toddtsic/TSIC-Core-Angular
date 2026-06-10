using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Adult/coach roles (Staff, UnassignedAdult, Referee, Recruiter) were FREE in legacy
/// (StaffTournamentController charged nothing) and have no fee-config UI or seed. So an
/// unconfigured fee resolution for these roles must default to $0 — NOT fail loud like the
/// paid Player/ClubRep paths, whose missing fee is a genuine revenue-leak misconfiguration.
///
/// These tests lock in that asymmetry so the fail-loud guard can't be re-attached to the
/// adult paths and silently break coach registration on every job again.
/// </summary>
public class AdultFeeUnconfiguredTests
{
    private static (FeeResolutionService Svc, FeeDataBuilder Builder,
        Guid JobId, Guid AgegroupId, Guid TeamId) Arrange()
    {
        var ctx = DbContextFactory.Create();
        var builder = new FeeDataBuilder(ctx);
        var job = builder.AddJob();
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "U12");
        var team = builder.AddTeam(job.JobId, ag.AgegroupId, "Hawks");

        var feeRepo = new FeeRepository(ctx);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object);
        var svc = new FeeResolutionService(feeRepo, jobRepo.Object, paymentState);
        return (svc, builder, job.JobId, ag.AgegroupId, team.TeamId);
    }

    private static Registrations NewReg(Guid jobId) => new()
    {
        RegistrationId = Guid.NewGuid(),
        JobId = jobId,
        FeeDonation = 0m,
        PaidTotal = 0m,
        Modified = DateTime.UtcNow
    };

    private static FeeApplicationContext Ctx => new() { AddProcessingFees = false };

    [Fact(DisplayName = "Staff: no JobFees row → stamps $0 (free), does not throw")]
    public async Task Staff_Unconfigured_StampsZero_DoesNotThrow()
    {
        var a = Arrange();   // NO Staff JobFee row added
        await a.Builder.SaveAsync();
        var reg = NewReg(a.JobId);

        var act = async () => await a.Svc.ApplyNewStaffRegistrationFeesAsync(
            reg, a.JobId, a.AgegroupId, a.TeamId, Ctx);

        await act.Should().NotThrowAsync();
        reg.FeeBase.Should().Be(0m);
        reg.FeeTotal.Should().Be(0m);
        reg.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Adult (Referee): no JobFees row → stamps $0 (free), does not throw")]
    public async Task Adult_Unconfigured_StampsZero_DoesNotThrow()
    {
        var a = Arrange();   // NO adult JobFee row added
        await a.Builder.SaveAsync();
        var reg = NewReg(a.JobId);

        var act = async () => await a.Svc.ApplyNewAdultRegistrationFeesAsync(
            reg, a.JobId, RoleConstants.Referee, Ctx);

        await act.Should().NotThrowAsync();
        reg.FeeBase.Should().Be(0m);
        reg.FeeTotal.Should().Be(0m);
        reg.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Staff: a configured JobFees row still prices normally (default-free does not disable charging)")]
    public async Task Staff_Configured_StillCharges()
    {
        var a = Arrange();
        // A manually-added Staff fee at agegroup level must still be honored.
        a.Builder.AddJobFee(a.JobId, RoleConstants.Staff, agegroupId: a.AgegroupId, deposit: 0m, balanceDue: 75m);
        await a.Builder.SaveAsync();
        var reg = NewReg(a.JobId);

        await a.Svc.ApplyNewStaffRegistrationFeesAsync(reg, a.JobId, a.AgegroupId, a.TeamId, Ctx);

        reg.FeeBase.Should().Be(75m);
        reg.OwedTotal.Should().Be(75m);
    }
}
