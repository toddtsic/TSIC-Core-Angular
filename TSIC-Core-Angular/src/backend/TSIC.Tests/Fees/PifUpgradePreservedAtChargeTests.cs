using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Regression: the at-charge late-fee realize (FeeResolutionService.RealizeLateFeeAtChargeAsync)
/// must NOT revert a freshly Pay-in-Full-upgraded registration back to the deposit.
///
/// Repro (deposit-phase job, single fresh player, "Pay in Full"): the payment flow first runs the
/// PIF upgrade — FeeBase = FullPrice, OwedTotal = full — then the CC charge engine runs
/// RealizeLateFeeAtChargeAsync on every reg before sizing the charge. That realize delegates to
/// ApplySwapFeesAsync, which re-derives deposit-vs-full phase from config + payment history. For a
/// brand-new reg nothing has been paid, so it lands on "deposit" and re-stamps FeeBase back DOWN to
/// the deposit (OwedTotal 527.85 → 207.00 in the field repro). The engine's AMOUNT_MISMATCH tripwire
/// then sees the deposit-sized owed against the full charge and refuses the payment.
///
/// Guarantee: an already-full-stamped reg stays full through the at-charge realize; a normal unpaid
/// deposit reg is untouched (still stamps to the deposit — the fix is narrow, not "force full").
///
/// Processing is OFF (AddJob default BAddProcessingFees = false), so the money math is exact.
/// Fee shape: Deposit $100 + BalanceDue $400 → FullPrice $500.
/// </summary>
public class PifUpgradePreservedAtChargeTests
{
    private const decimal Deposit = 100m;
    private const decimal BalanceDue = 400m;
    private const decimal FullPrice = Deposit + BalanceDue; // $500

    [Fact(DisplayName = "At-charge realize preserves a fresh Pay-in-Full upgrade (unpaid, full-stamped) — no revert to deposit")]
    public async Task RealizeAtCharge_PreservesPifUpgrade_DoesNotRevertToDeposit()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangeAsync(paid: 0m);

        // Parent chose Pay in Full → the real PIF upgrade stamps FeeBase = FullPrice (nothing paid yet).
        await svc.ApplyPifUpgradeAsync(reg, jobId, agId, teamId,
            new FeeApplicationContext { AddProcessingFees = false });
        reg.FeeBase.Should().Be(FullPrice, "precondition: the PIF upgrade stamped the full price");
        reg.OwedTotal.Should().Be(FullPrice);

        // The CC charge engine's at-charge late-fee realize must leave that stamp intact.
        await svc.RealizeLateFeeAtChargeAsync(reg, jobId, agId, teamId);

        reg.FeeBase.Should().Be(FullPrice, "at-charge realize must not revert a chosen Pay-in-Full to the deposit");
        reg.OwedTotal.Should().Be(FullPrice, "so the charge tripwire sees the full owed, not the deposit");
    }

    [Fact(DisplayName = "At-charge realize leaves a normal unpaid deposit charge at the deposit (fix stays narrow)")]
    public async Task RealizeAtCharge_NormalDeposit_StaysDeposit()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangeAsync(paid: 0m);

        // No PIF upgrade — an ordinary deposit-phase charge. The realize must keep it a deposit,
        // proving the preservation only rescues an already-full stamp and never forces full.
        await svc.RealizeLateFeeAtChargeAsync(reg, jobId, agId, teamId);

        reg.FeeBase.Should().Be(Deposit, "a normal unpaid deposit charge is unaffected");
        reg.OwedTotal.Should().Be(Deposit);
    }

    private static async Task<(FeeResolutionService Svc, Domain.Entities.Registrations Reg,
        Guid JobId, Guid AgegroupId, Guid TeamId)> ArrangeAsync(decimal paid)
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);   // ctor seeds the standard payment methods
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob();                      // BAddProcessingFees = false → processing OFF
        var league = acct.AddLeague(job.JobId);
        var ag = acct.AddAgegroup(league.LeagueId, "U12");
        var team = acct.AddTeam(job.JobId, ag.AgegroupId);

        fees.AddJobFee(job.JobId, RoleConstants.Player, leagueId: league.LeagueId, deposit: Deposit, balanceDue: BalanceDue);

        var reg = fees.AddRegistration(job.JobId, team.TeamId, paidTotal: paid);
        if (paid > 0m)
            acct.AddPayment(reg.RegistrationId, teamId: null, amount: paid, paymentMethodId: AccountingDataBuilder.CheckMethodId);

        await acct.SaveAsync();

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);   // present but inert — BAddProcessingFees is false
        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object);
        var svc = new FeeResolutionService(new FeeRepository(ctx), jobRepo.Object, paymentState);

        return (svc, reg, job.JobId, ag.AgegroupId, team.TeamId);
    }
}
