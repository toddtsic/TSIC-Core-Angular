using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Regression: a checkout Discount Code on a deposit-phase reg must not strand a $0.01 residual after
/// the deposit is paid in full. The bug was two proc-rounding paths disagreeing by a cent on the same
/// half-cent value:
///   • OLD discount apply (RegistrationFeeAdjustmentService.ReduceProcessingFeeProportionalAsync)
///     rounded the REDUCTION: Round(25 × 0.035) = Round(0.875) = 0.88 → proc 7.00 − 0.88 = 6.12.
///   • Canonical recompute (FeeResolutionService.ApplyRegistrationProcessingAndTotalsAsync, used by
///     the display shaper and the at-charge realize) rounds the WHOLE proc: Round(175 × 0.035) = 6.13.
/// Both use AwayFromZero — the split was round-the-part-then-subtract vs round-the-whole. The deposit
/// charge was sized on 181.12; the at-charge realize then re-stamped 181.13 AFTER the charge was
/// locked, stranding a cent. The fix routes the discount handler through the canonical recompute so
/// discount-time proc is 6.13, matching the charge basis and the realize.
///
/// Fee shape: Deposit 200 + BalanceDue 310, CC rate 3.5%, $25 code. Independent of PL-047: this reg
/// is deposit-phase (FeeBase 200 &lt; FullPrice 510), so the PreserveFullPaymentStamp term is inert.
/// </summary>
public class DepositDiscountProcRoundingTests
{
    private const decimal Deposit = 200m;
    private const decimal BalanceDue = 310m;
    private const decimal DiscountAmount = 25m;

    [Fact(DisplayName = "Deposit + $25 code + pay deposit → no stranded penny (discount proc = canonical, agrees with realize)")]
    public async Task DepositDiscount_PayDeposit_LeavesNoResidual()
    {
        var (feeSvc, reg, jobId, agId, teamId) = await ArrangeAsync();

        // 1. New deposit-phase stamp (canonical): FeeBase 200, proc 200 × 3.5% = 7.00, OwedTotal 207.
        await feeSvc.ApplyNewRegistrationFeesAsync(reg, jobId, agId, teamId,
            new FeeApplicationContext { IsFullPaymentRequired = false, AddProcessingFees = true });
        reg.FeeProcessing.Should().Be(7.00m, "precondition: canonical proc on the deposit");
        reg.OwedTotal.Should().Be(207.00m);

        // 2. Apply a $25 checkout Discount Code the way the FIXED handler does — stamp the discount,
        //    then recompute proc + totals canonically (RecomputeRegistrationFinancialsAsync). Proc must
        //    be Round(175 × 3.5%) = 6.13, the SAME value the display and the at-charge realize produce —
        //    NOT the proportional-shave 6.12 that stranded a penny.
        reg.FeeDiscount += DiscountAmount;
        await feeSvc.RecomputeRegistrationFinancialsAsync(reg, jobId);
        reg.FeeProcessing.Should().Be(6.13m, "discount proc must be the canonical Round(175 × 3.5%)");
        reg.OwedTotal.Should().Be(181.13m, "200 − 25 + 6.13");

        // 3. The deposit charge is sized here (ComputeChargesAsync caps the grossed deposit at OwedTotal).
        var chargeBasis = reg.OwedTotal; // 181.13 — exactly what hits the card

        // 4. The CC engine's at-charge realize re-stamps proc canonically — must AGREE with the charge basis.
        await feeSvc.RealizeLateFeeAtChargeAsync(reg, jobId, agId, teamId);

        // 5. Booking the charge basis fully clears the balance — no stranded cent.
        var residualAfterPayingCharge = reg.OwedTotal - chargeBasis;
        residualAfterPayingCharge.Should().Be(0m,
            "paying the deposit charge must fully clear owed — discount-time proc and realize proc now agree");
    }

    private static async Task<(FeeResolutionService FeeSvc,
        Domain.Entities.Registrations Reg, Guid JobId, Guid AgegroupId, Guid TeamId)> ArrangeAsync()
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob();
        var league = acct.AddLeague(job.JobId);
        var ag = acct.AddAgegroup(league.LeagueId, "U12");
        var team = acct.AddTeam(job.JobId, ag.AgegroupId);

        fees.AddJobFee(job.JobId, RoleConstants.Player, leagueId: league.LeagueId, deposit: Deposit, balanceDue: BalanceDue);

        var reg = fees.AddRegistration(job.JobId, team.TeamId, paidTotal: 0m);
        await acct.SaveAsync();

        // Processing ON at 3.5% CC / 1.5% eCheck — the rates that produce the 6.125 half-cent.
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = true,
                BApplyProcessingFeesToTeamDeposit = false,
                BTeamsFullPaymentRequired = false,
                PaymentMethodsAllowedCode = 7
            });
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        jobRepo.Setup(j => j.GetEcprocessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1.5m);

        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object);
        var feeSvc = new FeeResolutionService(new FeeRepository(ctx), jobRepo.Object, paymentState);

        return (feeSvc, reg, job.JobId, ag.AgegroupId, team.TeamId);
    }
}
