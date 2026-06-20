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
/// Paid-past-deposit promotion in the TEAM swap/reprice applier
/// (FeeResolutionService.ApplyTeamSwapFeesAsync) — the club-rep analog of
/// <see cref="PaidPastDepositPromotionTests"/> (which covers the player ApplySwapFeesAsync).
///
/// Phase is decided from BOTH the config cascade AND the team's own payments. This is what lets
/// the team reprice engine (RecalculateTeamFeesAsync) drop its old OwedTotal&lt;=0 skip without
/// minting bogus credits:
///   • a paid-ahead team re-stamps to FullPrice, never DOWN to the deposit (no bogus credit);
///   • a deposit→PIF upgrade reaches a team whose deposit-phase owed was already zeroed (e.g. by
///     a recorded deposit or a client correction) — it owes the new balance;
///   • a genuine over-payment surfaces as a (correct) negative OwedTotal;
///   • a team that paid EXACTLY its deposit is NOT promoted (stays a deposit-payer).
///
/// Processing is OFF (AccountingDataBuilder.AddJob → BAddProcessingFees false), so the money math
/// is exact integers and the assertions isolate the phase/promotion decision.
/// Fee shape: Deposit $100 + BalanceDue $400 → FullPrice $500.
/// </summary>
public class TeamPaidPastDepositPromotionTests
{
    private const decimal Deposit = 100m;
    private const decimal BalanceDue = 400m;
    private const decimal FullPrice = Deposit + BalanceDue; // $500

    [Fact(DisplayName = "Team paid EXACTLY the deposit → NOT promoted → stays deposit, owes nothing yet")]
    public async Task PaidExactlyDeposit_NotPromoted_StaysDeposit()
    {
        var (svc, team, jobId, agId) = await ArrangeAsync(paid: 100m, AccountingDataBuilder.CheckMethodId);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, DepositBaseline());

        team.FeeBase.Should().Be(Deposit, "paid exactly the deposit must not trip the promotion");
        team.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Team paid PAST the deposit → promoted to full price → owes the remaining balance")]
    public async Task PaidPastDeposit_Promoted_FullPrice()
    {
        var (svc, team, jobId, agId) = await ArrangeAsync(paid: 250m, AccountingDataBuilder.CheckMethodId);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, DepositBaseline());

        team.FeeBase.Should().Be(FullPrice, "paying past the deposit IS entering full payment");
        team.OwedTotal.Should().Be(250m, "500 full − 250 paid");
    }

    [Fact(DisplayName = "Correction-zeroed deposit team under an age-group PIF override → reprices to full price")]
    public async Task CorrectionZeroedDeposit_PifOverride_RepricesToFullPrice()
    {
        // The production bug: a $100 deposit posted as an "Online Correction By Client" nets
        // OwedTotal to 0 while still in deposit phase. The OLD engine skipped it as paid-in-full.
        // Now the applier re-stamps it under the age-group PIF override (job baseline stays
        // deposit — exactly like the real data where Jobs.BTeamsFullPaymentRequired = 0).
        var (svc, team, jobId, agId) = await ArrangeAsync(
            paid: 100m, AccountingDataBuilder.CorrectionMethodId, agegroupPhaseOverride: true);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, DepositBaseline());

        team.FeeBase.Should().Be(FullPrice, "PIF age-group override re-stamps even an owed-zeroed team");
        team.OwedTotal.Should().Be(400m, "500 full − 100 already paid = 400 still owed");
    }

    [Fact(DisplayName = "Correction PAST the deposit promotes (corrections count toward PrincipalPaid)")]
    public async Task CorrectionPastDeposit_Promoted_FullPrice()
    {
        // A correction is proc-free principal: PaymentState.NonProcCarryingPaid includes it, so a
        // correction past the deposit promotes the team just like a check would. Guards against a
        // regression where corrections stop counting toward the promotion threshold.
        var (svc, team, jobId, agId) = await ArrangeAsync(paid: 250m, AccountingDataBuilder.CorrectionMethodId);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, DepositBaseline());

        team.FeeBase.Should().Be(FullPrice, "a correction past the deposit IS entering full payment");
        team.OwedTotal.Should().Be(250m, "500 full − 250 correction-credited");
    }

    [Fact(DisplayName = "Config full-payment override promotes even an unpaid team")]
    public async Task ConfigFullPayment_Promotes_EvenWhenUnpaid()
    {
        var (svc, team, jobId, agId) = await ArrangeAsync(paid: 0m, AccountingDataBuilder.CheckMethodId, agegroupPhaseOverride: true);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, DepositBaseline());

        team.FeeBase.Should().Be(FullPrice, "age-group override forces full payment regardless of payments");
        team.OwedTotal.Should().Be(FullPrice);
    }

    [Fact(DisplayName = "PIF→deposit downgrade of a paid-in-full team → stays full price, no bogus credit")]
    public async Task PifDowngrade_PaidInFull_StaysFullPrice_NoNegativeCredit()
    {
        // The reason it is safe to remove the engine's OwedTotal<=0 skip: without the promotion,
        // a deposit-baseline reprice would re-stamp FeeBase DOWN to $100 and mint a −$400 credit.
        var (svc, team, jobId, agId) = await ArrangeAsync(paid: 500m, AccountingDataBuilder.CheckMethodId);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, DepositBaseline());

        team.FeeBase.Should().Be(FullPrice, "a paid-ahead team is never re-stamped down to deposit");
        team.OwedTotal.Should().Be(0m, "500 full − 500 paid; not a negative credit");
    }

    [Fact(DisplayName = "Over-payment surfaces a truthful negative OwedTotal (credit)")]
    public async Task OverPaid_SurfacesNegativeOwed()
    {
        var (svc, team, jobId, agId) = await ArrangeAsync(paid: 600m, AccountingDataBuilder.CheckMethodId);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, DepositBaseline());

        team.FeeBase.Should().Be(FullPrice, "paid past deposit → full price");
        team.OwedTotal.Should().Be(-100m, "500 full − 600 paid = a real $100 credit, surfaced not clamped");
    }

    private static TeamFeeApplicationContext DepositBaseline() =>
        new() { IsFullPaymentRequired = false, AddProcessingFees = false, ProcessingFeePercent = 0m };

    private static async Task<(FeeResolutionService Svc, Domain.Entities.Teams Team, Guid JobId, Guid AgegroupId)>
        ArrangeAsync(decimal paid, Guid paymentMethodId, bool? agegroupPhaseOverride = null)
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);   // ctor seeds the standard payment methods
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob();                      // BAddProcessingFees = false → processing OFF
        var league = acct.AddLeague(job.JobId);
        var ag = acct.AddAgegroup(league.LeagueId, "U12");
        // Start in deposit phase ($100) with PaidTotal mirroring the ledger payment.
        var team = acct.AddTeam(job.JobId, ag.AgegroupId, teamName: "Hawks", feeBase: Deposit, paidTotal: paid);

        // League-scoped ClubRep fee carries the deposit/balance; an optional age-group row sets the
        // phase override (mirrors "age group set to PIF" — job baseline stays deposit).
        fees.AddJobFee(job.JobId, RoleConstants.ClubRep, leagueId: league.LeagueId, deposit: Deposit, balanceDue: BalanceDue);
        if (agegroupPhaseOverride.HasValue)
            fees.AddJobFee(job.JobId, RoleConstants.ClubRep, agegroupId: ag.AgegroupId,
                bFullPaymentRequired: agegroupPhaseOverride);

        if (paid > 0m)
            acct.AddPayment(registrationId: null, teamId: team.TeamId, amount: paid, paymentMethodId: paymentMethodId);

        await acct.SaveAsync();

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);   // present but inert — BAddProcessingFees is false
        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object);
        var svc = new FeeResolutionService(new FeeRepository(ctx), jobRepo.Object, paymentState);

        return (svc, team, job.JobId, ag.AgegroupId);
    }
}
