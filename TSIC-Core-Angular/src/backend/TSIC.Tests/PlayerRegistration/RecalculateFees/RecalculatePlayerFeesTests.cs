using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.Tests.PlayerRegistration.RecalculateFees;

/// <summary>
/// Engine contract for RecalculatePlayerFeesAsync.
///
/// The engine no longer carries a paid-in-full skip. It hands EVERY active player
/// registration to the fee applier (ApplySwapFeesAsync) and forwards the JOB-LEVEL phase
/// baseline; the per-scope override AND the paid-past-deposit promotion are resolved
/// DOWNSTREAM in FeeResolutionService. Removing the old OwedTotal&lt;=0 gate is what lets a
/// fee/price increase reach already-paid registrants — credit-safety (never re-stamping a
/// paid-ahead reg DOWN to a deposit) is the applier's job, proven in
/// PaidPastDepositPromotionTests / PaymentPhaseResolutionTests.
/// </summary>
public class RecalculatePlayerFeesTests
{
    private const decimal DepositAmt = 200m;
    private const decimal BalanceDueAmt = 310m;
    private const decimal FullAmt = DepositAmt + BalanceDueAmt; // $510
    private const decimal PifPaid = 527.85m;
    private const decimal DepositPaid = 207m;

    [Fact(DisplayName = "Engine reprices EVERY active reg — no paid-in-full skip")]
    public async Task RecalculatePlayerFees_RepricesAllActiveRegs_NoSkip()
    {
        var jobId = Guid.NewGuid();
        var agegroupId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        // Paid-in-full, deposit-only-paid, and unpaid regs — under the new contract all three
        // go through the applier (the applier decides what, if anything, changes).
        var pifReg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            FeeBase = FullAmt,
            FeeProcessing = 17.85m,
            FeeTotal = PifPaid,
            PaidTotal = PifPaid,
            OwedTotal = 0m,
            BActive = true,
        };
        var depositReg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            FeeBase = FullAmt,
            FeeProcessing = 17.85m,
            FeeTotal = PifPaid,
            PaidTotal = DepositPaid,
            OwedTotal = PifPaid - DepositPaid,
            BActive = true,
        };
        var unpaidReg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            FeeBase = DepositAmt,
            FeeProcessing = 7m,
            FeeTotal = DepositPaid,
            PaidTotal = 0m,
            OwedTotal = DepositPaid,
            BActive = true,
        };

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo { BPlayersFullPaymentRequired = false });

        var regRepo = new Mock<IRegistrationRepository>();
        regRepo.Setup(r => r.GetActivePlayerRegistrationsByJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { pifReg, depositReg, unpaidReg });

        // Agegroup resolves through the team — map each reg's team to its agegroup.
        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsWithDetailsForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { new() { TeamId = teamId, AgegroupId = agegroupId } });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.ResolveFeeAsync(
                jobId, RoleConstants.Player, agegroupId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { Deposit = DepositAmt, BalanceDue = BalanceDueAmt });

        // Mutating stub so the engine counts an update and exercises SaveChanges.
        feeService.Setup(f => f.ApplySwapFeesAsync(
                It.IsAny<Registrations>(), jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Returns((Registrations reg, Guid _, Guid _, Guid _, FeeApplicationContext _, CancellationToken _) =>
            {
                reg.FeeBase = FullAmt;
                reg.FeeProcessing = 17.85m;
                return Task.CompletedTask;
            });

        var svc = BuildService(feeService, regRepo, teamRepo, jobRepo);

        var updated = await svc.RecalculatePlayerFeesAsync(jobId, "test-user");

        // Every active reg is handed to the applier — including the paid-in-full one.
        foreach (var reg in new[] { pifReg, depositReg, unpaidReg })
        {
            feeService.Verify(f => f.ApplySwapFeesAsync(
                    reg, jobId, agegroupId, teamId,
                    It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
                Times.Once, "no paid-in-full skip — every active reg is repriced");
        }

        // unpaidReg's FeeBase moved 200 -> 510, so it counts as updated.
        updated.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The recalc engine forwards the JOB-LEVEL baseline to the fee applier; the per-scope
    /// override (JobFees.BFullPaymentRequired, team → agegroup → league) is applied DOWNSTREAM
    /// inside FeeResolutionService via the canonical ResolvedFee.ResolveFullPaymentPhase
    /// chokepoint — not pre-resolved by the engine. This pins the engine's contract: it hands
    /// the baseline through and does NOT short-circuit resolution itself.
    /// </summary>
    [Theory(DisplayName = "Recalc engine forwards the job baseline to the applier (override resolved downstream)")]
    [InlineData(false, true)]   // override present but engine still forwards the baseline…
    [InlineData(true, false)]   // …in both directions
    [InlineData(false, null)]   // no override → baseline
    [InlineData(true, null)]    // no override → baseline
    public async Task RecalculatePlayerFees_ForwardsJobBaseline_OverrideResolvedDownstream(
        bool jobBaseline, bool? scopeOverride)
    {
        var expectedEffective = jobBaseline;
        var jobId = Guid.NewGuid();
        var agegroupId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            FeeBase = FullAmt,
            FeeProcessing = 17.85m,
            FeeTotal = PifPaid,
            PaidTotal = DepositPaid,
            OwedTotal = PifPaid - DepositPaid,
            BActive = true,
        };

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo { BPlayersFullPaymentRequired = jobBaseline });

        var regRepo = new Mock<IRegistrationRepository>();
        regRepo.Setup(r => r.GetActivePlayerRegistrationsByJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { reg });

        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsWithDetailsForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { new() { TeamId = teamId, AgegroupId = agegroupId } });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.ResolveFeeAsync(
                jobId, RoleConstants.Player, agegroupId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee
            {
                FeeConfigured = true,
                Deposit = DepositAmt,
                BalanceDue = BalanceDueAmt,
                BFullPaymentRequired = scopeOverride
            });

        // Capture-only stub (no mutation → no SaveChanges needed). We assert the phase the
        // engine handed the applier, not what the applier then does with it.
        FeeApplicationContext? captured = null;
        feeService.Setup(f => f.ApplySwapFeesAsync(
                It.IsAny<Registrations>(), jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Returns((Registrations _, Guid _, Guid _, Guid _, FeeApplicationContext ctx, CancellationToken _) =>
            {
                captured = ctx;
                return Task.CompletedTask;
            });

        var svc = BuildService(feeService, regRepo, teamRepo, jobRepo);

        await svc.RecalculatePlayerFeesAsync(jobId, "test-user");

        captured.Should().NotBeNull("every active reg is handed to the applier");
        captured!.IsFullPaymentRequired.Should().Be(expectedEffective);
    }

    private static PlayerRegistrationService BuildService(
        Mock<IFeeResolutionService> feeService,
        Mock<IRegistrationRepository> regRepo,
        Mock<ITeamRepository> teamRepo,
        Mock<IJobRepository> jobRepo) =>
        new(
            new Mock<ILogger<PlayerRegistrationService>>().Object,
            feeService.Object,
            new Mock<IVerticalInsureService>().Object,
            new Mock<ITeamLookupService>().Object,
            new Mock<IPlayerFormValidationService>().Object,
            regRepo.Object,
            teamRepo.Object,
            jobRepo.Object,
            new Mock<ITeamPlacementService>().Object,
            new Mock<IMedFormService>().Object,
            new Mock<TSIC.API.Services.Shared.UsLax.IUsLaxService>().Object);
}
