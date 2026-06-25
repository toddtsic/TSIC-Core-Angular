using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Phase 2 — charge-entry realize (<c>IFeeResolutionService.RealizeLateFeeAtChargeAsync</c>). This is
/// what makes an AUTO-ACTIVATED late-fee window (a window whose start date passed with NO director
/// reprice) land at payment: the charge path re-derives the live late fee and re-stamps OwedTotal
/// just before sizing the charge, so the previewed-on-refresh total and the charged total are the
/// same stamped number (the AMOUNT_MISMATCH tripwire can't fire spuriously).
///
/// The realize is the read/charge twin of the reprice engine — it delegates to the SAME swap applier
/// with AssessActiveLateFee=true, building the per-role context from job settings instead of an
/// explicit ctx. These tests pin the realize's own contract: it assesses a live fee on a still-owing
/// record, leaves a paid-in-full record alone, and never doubles on repeat (idempotent — the charge
/// path may run it more than once across retries).
///
/// Same harness/shape as <see cref="LateFeeRetroAssessmentTests"/>: processing OFF for exact integers,
/// Deposit $100 + Balance $400 → FullPrice $500, late fee $50 on an unbounded (always-active) window
/// so the assertions don't depend on the wall clock. The mocked IJobRepository returns null for the
/// settings/baseline accessors → deposit-phase baselines (false), matching the explicit-ctx retro tests.
/// </summary>
public class LateFeeRealizeAtChargeTests
{
    private const decimal Deposit = 100m;
    private const decimal BalanceDue = 400m;
    private const decimal FullPrice = Deposit + BalanceDue; // $500
    private const decimal LateFee = 50m;

    // ── Team (club-rep) path ──

    [Fact(DisplayName = "Realize: deposit-paid team, active window, never repriced → late fee lands at charge")]
    public async Task Team_AutoActivatedWindow_RealizedAtCharge()
    {
        var (svc, team, jobId, _) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: 0m);

        await svc.RealizeLateFeeAtChargeAsync(team, jobId);

        team.FeeLatefee.Should().Be(LateFee, "an open window + still-owing principal assesses the live fee");
        team.OwedTotal.Should().Be(LateFee, "100 deposit + 50 late − 100 paid = 50 now owed at the register");
    }

    [Fact(DisplayName = "Realize: paid-in-full team → no late fee (no spurious charge)")]
    public async Task Team_PaidInFull_NoRealize()
    {
        var (svc, team, jobId, _) = await ArrangeTeamAsync(paid: FullPrice, existingLateFee: 0m);

        await svc.RealizeLateFeeAtChargeAsync(team, jobId);

        team.FeeLatefee.Should().Be(0m, "a paid-in-full record is never assessed a late fee (PIF-exempt)");
        team.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Realize is idempotent: running it twice never doubles the late fee")]
    public async Task Team_Realize_Idempotent()
    {
        var (svc, team, jobId, _) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: 0m);

        await svc.RealizeLateFeeAtChargeAsync(team, jobId);
        await svc.RealizeLateFeeAtChargeAsync(team, jobId);

        team.FeeLatefee.Should().Be(LateFee, "the second realize re-derives the SAME live fee, never stacks it");
        team.OwedTotal.Should().Be(LateFee);
    }

    // ── Player path (symmetric) ──

    [Fact(DisplayName = "Realize: deposit-paid player reg, active window, never repriced → late fee lands at charge")]
    public async Task Player_AutoActivatedWindow_RealizedAtCharge()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: Deposit, existingLateFee: 0m);

        await svc.RealizeLateFeeAtChargeAsync(reg, jobId, agId, teamId);

        reg.FeeLatefee.Should().Be(LateFee);
        reg.OwedTotal.Should().Be(LateFee, "100 deposit + 50 late − 100 paid");
    }

    [Fact(DisplayName = "Realize: paid-in-full player reg → no late fee")]
    public async Task Player_PaidInFull_NoRealize()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: FullPrice, existingLateFee: 0m);

        await svc.RealizeLateFeeAtChargeAsync(reg, jobId, agId, teamId);

        reg.FeeLatefee.Should().Be(0m);
    }

    // ── Arrange helpers (mirror LateFeeRetroAssessmentTests) ──

    private static async Task<(FeeResolutionService Svc, Domain.Entities.Teams Team, Guid JobId, Guid AgegroupId)>
        ArrangeTeamAsync(decimal paid, decimal existingLateFee)
    {
        var (ctx, jobId, leagueId, agId, acct, fees) = SeedJob();

        var team = acct.AddTeam(jobId, agId, teamName: "Hawks", feeBase: Deposit, paidTotal: paid);
        team.FeeLatefee = existingLateFee;
        team.RecalcTotals();

        var clubFee = fees.AddJobFee(jobId, RoleConstants.ClubRep, leagueId: leagueId, deposit: Deposit, balanceDue: BalanceDue);
        fees.AddModifier(clubFee.JobFeeId, FeeConstants.ModifierLateFee, LateFee); // unbounded → active now

        if (paid > 0m)
            acct.AddPayment(registrationId: null, teamId: team.TeamId, amount: paid, paymentMethodId: AccountingDataBuilder.CheckMethodId);

        await acct.SaveAsync();

        return (BuildService(ctx), team, jobId, agId);
    }

    private static async Task<(FeeResolutionService Svc, Domain.Entities.Registrations Reg, Guid JobId, Guid AgegroupId, Guid TeamId)>
        ArrangePlayerAsync(decimal paid, decimal existingLateFee)
    {
        var (ctx, jobId, leagueId, agId, acct, fees) = SeedJob();

        var team = acct.AddTeam(jobId, agId, teamName: "Hawks", feeBase: Deposit, paidTotal: 0m);
        var reg = fees.AddRegistration(jobId, team.TeamId, feeBase: Deposit, feeLatefee: existingLateFee, paidTotal: paid);

        var playerFee = fees.AddJobFee(jobId, RoleConstants.Player, leagueId: leagueId, deposit: Deposit, balanceDue: BalanceDue);
        fees.AddModifier(playerFee.JobFeeId, FeeConstants.ModifierLateFee, LateFee); // unbounded → active now

        if (paid > 0m)
            acct.AddPayment(registrationId: reg.RegistrationId, teamId: null, amount: paid, paymentMethodId: AccountingDataBuilder.CheckMethodId);

        await acct.SaveAsync();

        return (BuildService(ctx), reg, jobId, agId, team.TeamId);
    }

    private static (Infrastructure.Data.SqlDbContext.SqlDbContext Ctx, Guid JobId, Guid LeagueId, Guid AgegroupId,
        AccountingDataBuilder Acct, FeeDataBuilder Fees) SeedJob()
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx); // seeds payment methods; BAddProcessingFees = false
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob();
        var league = acct.AddLeague(job.JobId);
        var ag = acct.AddAgegroup(league.LeagueId, "U12");
        return (ctx, job.JobId, league.LeagueId, ag.AgegroupId, acct, fees);
    }

    private static FeeResolutionService BuildService(Infrastructure.Data.SqlDbContext.SqlDbContext ctx)
    {
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m); // present but inert — processing is OFF
        var paymentState = new PaymentStateService(new RegistrationAccountingRepository(ctx), jobRepo.Object);
        return new FeeResolutionService(new FeeRepository(ctx), jobRepo.Object, paymentState);
    }
}
