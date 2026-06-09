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
/// Verifies the skip guard in RecalculatePlayerFeesAsync: any registration whose
/// PaidTotal already covers the full price (Deposit + BalanceDue) must be left
/// untouched. Otherwise the unwind (BPlayersFullPaymentRequired true→false) would
/// re-stamp FeeBase = deposit, and combined with the existing PaidTotal this
/// produces OwedTotal &lt; 0 — a bogus credit balance for voluntary-PIF
/// registrations and any balance-due-phase registrant who already paid in full.
/// </summary>
public class RecalculatePlayerFeesTests
{
    private const decimal DepositAmt = 200m;
    private const decimal BalanceDueAmt = 310m;
    private const decimal FullAmt = DepositAmt + BalanceDueAmt; // $510
    private const decimal PifPaid = 527.85m;
    private const decimal DepositPaid = 207m;

    [Fact(DisplayName = "Skip guard: paid-in-full registration is NOT touched on flag unflip")]
    public async Task RecalculatePlayerFees_PaidInFull_SkippedFromRestamp()
    {
        var jobId = Guid.NewGuid();
        var agegroupId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

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
            FeeBase = DepositAmt,
            FeeProcessing = 7m,
            FeeTotal = DepositPaid,
            PaidTotal = DepositPaid,
            OwedTotal = 0m,
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

        // Agegroup now resolves through the team — map each reg's team to its agegroup.
        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsWithDetailsForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { new() { TeamId = teamId, AgegroupId = agegroupId } });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.ResolveFeeAsync(
                jobId, RoleConstants.Player, agegroupId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { Deposit = DepositAmt, BalanceDue = BalanceDueAmt });

        // Stub ApplySwapFeesAsync to mutate the reg the same way the real method
        // would on a deposit-phase recalc. If this runs against the PIF reg, the
        // test fails — that's the bug we're guarding.
        feeService.Setup(f => f.ApplySwapFeesAsync(
                It.IsAny<Registrations>(), jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Returns((Registrations reg, Guid _, Guid _, Guid _, FeeApplicationContext _, CancellationToken _) =>
            {
                reg.FeeBase = DepositAmt;
                reg.FeeProcessing = 7m;
                reg.FeeTotal = DepositPaid;
                reg.OwedTotal = DepositPaid - reg.PaidTotal;
                return Task.CompletedTask;
            });

        var svc = new PlayerRegistrationService(
            new Mock<ILogger<PlayerRegistrationService>>().Object,
            feeService.Object,
            new Mock<IVerticalInsureService>().Object,
            new Mock<ITeamLookupService>().Object,
            new Mock<IPlayerFormValidationService>().Object,
            regRepo.Object,
            teamRepo.Object,
            jobRepo.Object,
            new Mock<ITeamPlacementService>().Object,
            new Mock<IMedFormService>().Object);

        await svc.RecalculatePlayerFeesAsync(jobId, "test-user");

        // PIF reg untouched — paid >= full short-circuited the recalc.
        pifReg.FeeBase.Should().Be(FullAmt, "PIF registration must be skipped");
        pifReg.FeeProcessing.Should().Be(17.85m);
        pifReg.PaidTotal.Should().Be(PifPaid);
        pifReg.OwedTotal.Should().Be(0m, "no bogus credit");

        feeService.Verify(f => f.ApplySwapFeesAsync(
                pifReg, jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
            Times.Never, "PIF reg must NOT be re-stamped");

        // Deposit-paid and unpaid regs still go through the recalc path.
        feeService.Verify(f => f.ApplySwapFeesAsync(
                depositReg, jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
        feeService.Verify(f => f.ApplySwapFeesAsync(
                unpaidReg, jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The recalc engine forwards the JOB-LEVEL baseline to the fee applier; the per-scope
    /// override (JobFees.BFullPaymentRequired, team → agegroup → league) is applied DOWNSTREAM
    /// inside FeeResolutionService via the canonical ResolvedFee.ResolveFullPaymentPhase
    /// chokepoint — not pre-resolved by the engine. (That a per-scope override actually flips
    /// the stamped FeeBase is proven against the real applier in PaymentPhaseResolutionTests.)
    /// This test pins the engine's contract: it hands the baseline through and does NOT
    /// short-circuit resolution itself, even when a scope override is present on the resolved fee.
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

        // Deposit-paid (207 < full 510) → skip guard does not fire → reg is re-priced.
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            FeeBase = DepositAmt,
            FeeProcessing = 7m,
            FeeTotal = DepositPaid,
            PaidTotal = DepositPaid,
            OwedTotal = 0m,
            BActive = true,
        };

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo { BPlayersFullPaymentRequired = jobBaseline });

        var regRepo = new Mock<IRegistrationRepository>();
        regRepo.Setup(r => r.GetActivePlayerRegistrationsByJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { reg });

        // Agegroup now resolves through the team — map the reg's team to its agegroup.
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

        // Capture-only stub (no mutation → no SaveChanges needed). We assert what phase
        // the engine handed the fee applier, not what the applier then does with it.
        FeeApplicationContext? captured = null;
        feeService.Setup(f => f.ApplySwapFeesAsync(
                It.IsAny<Registrations>(), jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Returns((Registrations _, Guid _, Guid _, Guid _, FeeApplicationContext ctx, CancellationToken _) =>
            {
                captured = ctx;
                return Task.CompletedTask;
            });

        var svc = new PlayerRegistrationService(
            new Mock<ILogger<PlayerRegistrationService>>().Object,
            feeService.Object,
            new Mock<IVerticalInsureService>().Object,
            new Mock<ITeamLookupService>().Object,
            new Mock<IPlayerFormValidationService>().Object,
            regRepo.Object,
            teamRepo.Object,
            jobRepo.Object,
            new Mock<ITeamPlacementService>().Object,
            new Mock<IMedFormService>().Object);

        await svc.RecalculatePlayerFeesAsync(jobId, "test-user");

        captured.Should().NotBeNull("the deposit-paid reg is below full and must be re-priced");
        captured!.IsFullPaymentRequired.Should().Be(expectedEffective);
    }
}
