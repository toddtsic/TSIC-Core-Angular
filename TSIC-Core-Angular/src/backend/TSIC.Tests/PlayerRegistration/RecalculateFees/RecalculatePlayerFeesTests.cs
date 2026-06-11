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
/// Verifies the skip guard in RecalculatePlayerFeesAsync: any registration that is
/// already fully settled (OwedTotal &lt;= 0) must be left untouched. Otherwise the
/// unwind (BPlayersFullPaymentRequired true→false) would re-stamp FeeBase = deposit,
/// and combined with the existing PaidTotal this produces OwedTotal &lt; 0 — a bogus
/// credit balance for voluntary-PIF registrations and any balance-due-phase registrant
/// who already paid in full.
///
/// The guard tests OwedTotal (which already nets FeeProcessing and FeeDiscount), NOT
/// PaidTotal against the bare resolved deposit+balanceDue — that older comparison
/// misclassified any registrant carrying a discount code (see the discount-code test).
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
        // Full-payment-phase registrant who paid only the deposit and still owes the
        // balance (OwedTotal > 0) — the row that MUST be re-priced down to deposit.
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
    /// Regression for the discount-code reversion bug (Ann's scenario). A registrant who paid
    /// in full at a DISCOUNTED rate has PaidTotal = principal + proc − discount, which is LESS
    /// than the bare resolved deposit+balanceDue. The old guard (PaidTotal >= deposit+balanceDue)
    /// therefore classified that fully-settled registrant as NOT paid in full and re-stamped it
    /// to deposit on the unflip — minting a bogus credit. The corrected guard tests OwedTotal,
    /// which already nets the discount and proc, so:
    ///   • a discounted PIF reg (OwedTotal == 0) is SKIPPED, and
    ///   • a discounted deposit-payer still owing a balance (OwedTotal > 0) is RE-PRICED.
    /// </summary>
    [Fact(DisplayName = "Skip guard: discount-code PIF reg is skipped; discount-code deposit reg is re-priced")]
    public async Task RecalculatePlayerFees_DiscountCode_SkipsPaidInFull_RepricesStillOwing()
    {
        var jobId = Guid.NewGuid();
        var agegroupId = Guid.NewGuid();
        var teamId = Guid.NewGuid();

        const decimal Discount = 100m;
        const decimal DiscountedProc = 14m;
        // FeeTotal = FeeBase(full) + proc − discount.
        const decimal DiscountedFeeTotal = FullAmt + DiscountedProc - Discount; // $424

        // Paid the full discounted amount → OwedTotal 0. PaidTotal ($424) is below the bare
        // resolved full price ($510), so the OLD PaidTotal>=fullAmount guard would have MISSED
        // this and wrongly re-stamped it.
        var dcPifReg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            FeeBase = FullAmt,
            FeeProcessing = DiscountedProc,
            FeeDiscount = Discount,
            FeeTotal = DiscountedFeeTotal,
            PaidTotal = DiscountedFeeTotal,
            OwedTotal = 0m,
            BActive = true,
        };
        // Discounted registrant who paid only the deposit and still owes a balance.
        var dcDepositReg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            AssignedTeamId = teamId,
            FeeBase = FullAmt,
            FeeProcessing = DiscountedProc,
            FeeDiscount = Discount,
            FeeTotal = DiscountedFeeTotal,
            PaidTotal = DepositPaid,
            OwedTotal = DiscountedFeeTotal - DepositPaid,
            BActive = true,
        };

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobPaymentInfoAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPaymentInfo { BPlayersFullPaymentRequired = false });

        var regRepo = new Mock<IRegistrationRepository>();
        regRepo.Setup(r => r.GetActivePlayerRegistrationsByJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations> { dcPifReg, dcDepositReg });

        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsWithDetailsForJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { new() { TeamId = teamId, AgegroupId = agegroupId } });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.ResolveFeeAsync(
                jobId, RoleConstants.Player, agegroupId, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedFee { Deposit = DepositAmt, BalanceDue = BalanceDueAmt });

        // Re-stamp to deposit, preserving the discount (matches the real applier).
        feeService.Setup(f => f.ApplySwapFeesAsync(
                It.IsAny<Registrations>(), jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Returns((Registrations reg, Guid _, Guid _, Guid _, FeeApplicationContext _, CancellationToken _) =>
            {
                reg.FeeBase = DepositAmt;
                reg.FeeProcessing = 7m;
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

        // Discounted PIF reg untouched — no bogus credit, discount preserved.
        dcPifReg.FeeBase.Should().Be(FullAmt, "fully-settled discount reg must be skipped");
        dcPifReg.FeeDiscount.Should().Be(Discount);
        dcPifReg.OwedTotal.Should().Be(0m, "no bogus credit");
        feeService.Verify(f => f.ApplySwapFeesAsync(
                dcPifReg, jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
            Times.Never, "discount-code PIF reg must NOT be re-stamped");

        // Discounted deposit-payer still owing is re-priced as normal.
        feeService.Verify(f => f.ApplySwapFeesAsync(
                dcDepositReg, jobId, agegroupId, teamId,
                It.IsAny<FeeApplicationContext>(), It.IsAny<CancellationToken>()),
            Times.Once, "discount-code reg still owing a balance must be re-priced");
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

        // Full-payment-phase deposit-payer still owing a balance (OwedTotal > 0) → skip guard
        // does not fire → reg is re-priced.
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
