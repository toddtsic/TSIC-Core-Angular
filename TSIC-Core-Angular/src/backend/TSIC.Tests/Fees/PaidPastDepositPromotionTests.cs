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
/// Paid-past-deposit promotion in the swap/reprice applier (FeeResolutionService.ApplySwapFeesAsync).
///
/// Phase is decided from BOTH the config cascade AND the registrant's own payments: having paid
/// PAST the deposit tier IS entering full payment. This guarantees:
///   • a paid-ahead reg is re-stamped to FullPrice, never DOWN to the deposit (no bogus credit);
///   • a price increase reaches already-paid registrants — they owe the delta;
///   • a genuine over-payment surfaces as a (correct) negative OwedTotal;
///   • a reg that paid EXACTLY its deposit is NOT promoted (stays a deposit-payer, owes nothing yet).
///
/// Processing is OFF here (GetJobFeeSettingsAsync unmocked → BAddProcessingFees false), so the
/// money math is exact integers and the assertions isolate the phase/promotion decision.
/// Fee shape: Deposit $100 + BalanceDue $400 → FullPrice $500.
/// </summary>
public class PaidPastDepositPromotionTests
{
    private const decimal Deposit = 100m;
    private const decimal BalanceDue = 400m;
    private const decimal FullPrice = Deposit + BalanceDue; // $500

    [Fact(DisplayName = "Paid EXACTLY the deposit → NOT promoted → stays deposit, owes nothing yet")]
    public async Task PaidExactlyDeposit_NotPromoted_StaysDeposit()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangeSwapAsync(paid: 100m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, DepositBaseline());

        reg.FeeBase.Should().Be(Deposit, "paid exactly the deposit must not trip the promotion");
        reg.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Paid PAST the deposit → promoted to full price → owes the remaining balance")]
    public async Task PaidPastDeposit_Promoted_FullPrice()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangeSwapAsync(paid: 250m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, DepositBaseline());

        reg.FeeBase.Should().Be(FullPrice, "paying past the deposit IS entering full payment");
        reg.OwedTotal.Should().Be(250m, "500 full − 250 paid");
    }

    [Fact(DisplayName = "Paid nothing → deposit phase (deposit-only base)")]
    public async Task PaidNothing_StaysDeposit()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangeSwapAsync(paid: 0m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, DepositBaseline());

        reg.FeeBase.Should().Be(Deposit);
        reg.OwedTotal.Should().Be(Deposit, "unpaid deposit-phase reg owes the deposit");
    }

    [Fact(DisplayName = "Config full-payment override promotes even an unpaid reg")]
    public async Task ConfigFullPayment_Promotes_EvenWhenUnpaid()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangeSwapAsync(paid: 0m, teamPhaseOverride: true);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, DepositBaseline());

        reg.FeeBase.Should().Be(FullPrice, "team-scope override forces full payment regardless of payments");
        reg.OwedTotal.Should().Be(FullPrice);
    }

    [Fact(DisplayName = "Over-payment surfaces a truthful negative OwedTotal (credit)")]
    public async Task OverPaid_SurfacesNegativeOwed()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangeSwapAsync(paid: 600m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, DepositBaseline());

        reg.FeeBase.Should().Be(FullPrice, "paid past deposit → full price");
        reg.OwedTotal.Should().Be(-100m, "500 full − 600 paid = a real $100 credit, surfaced not clamped");
    }

    private static FeeApplicationContext DepositBaseline() =>
        new() { IsFullPaymentRequired = false, AddProcessingFees = false };

    private static async Task<(FeeResolutionService Svc, Domain.Entities.Registrations Reg,
        Guid JobId, Guid AgegroupId, Guid TeamId)> ArrangeSwapAsync(
        decimal paid, bool? teamPhaseOverride = null)
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);   // ctor seeds the standard payment methods
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob();                      // BAddProcessingFees = false → processing OFF
        var league = acct.AddLeague(job.JobId);
        var ag = acct.AddAgegroup(league.LeagueId, "U12");
        var team = acct.AddTeam(job.JobId, ag.AgegroupId);

        // League-scoped fee carries the deposit/balance; an optional team row sets the phase override.
        fees.AddJobFee(job.JobId, RoleConstants.Player, leagueId: league.LeagueId, deposit: Deposit, balanceDue: BalanceDue);
        if (teamPhaseOverride.HasValue)
            fees.AddJobFee(job.JobId, RoleConstants.Player, agegroupId: ag.AgegroupId, teamId: team.TeamId,
                bFullPaymentRequired: teamPhaseOverride);

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
