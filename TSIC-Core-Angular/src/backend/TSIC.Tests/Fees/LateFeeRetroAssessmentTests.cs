using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// Late-fee assessment in the swap appliers, gated by the <c>AssessActiveLateFee</c> context flag.
/// A late fee is purely a consequence of payment: the flag is set ONLY at charge entry
/// (<see cref="FeeResolutionService.RealizeLateFeeAtChargeAsync(TSIC.Domain.Entities.Teams, System.Guid, System.Threading.CancellationToken)"/>
/// and its player twin), so a late fee mints when a record actually pays — never on a director's
/// reprice, which recomputes base/phase/processing only and leaves the late fee frozen. Without the
/// flag every modifier stays frozen (covered by <see cref="PlayerRegistration.EarlyBirdLateFee.EarlyBirdLateFeeTests"/>).
///
/// Product rule (the assessment the flag performs, at charge): mint a currently-active late fee onto
/// a record that
///   (a) carries NO collected late fee yet, AND
///   (b) still owes principal against the FULL price (not paid in full).
/// It never climbs a late fee already paid, never touches a paid-in-full record, and never disturbs a
/// frozen discount/donation. This closes Ann's case from the other direction: a team that paid its
/// deposit before a late fee existed is assessed that fee when it returns to pay the balance — at that
/// payment, not before it.
///
/// Processing is OFF (AccountingDataBuilder → BAddProcessingFees false) so the money math is
/// exact integers. Fee shape: Deposit $100 + Balance $400 → FullPrice $500. Late fee $50,
/// unbounded window (always active) so the assertions don't depend on the wall clock.
/// </summary>
public class LateFeeRetroAssessmentTests
{
    private const decimal Deposit = 100m;
    private const decimal BalanceDue = 400m;
    private const decimal FullPrice = Deposit + BalanceDue; // $500
    private const decimal LateFee = 50m;

    // ── Team (club-rep) path: ApplyTeamSwapFeesAsync ──

    [Fact(DisplayName = "Charge realize: deposit-paid team with no late fee + still owing → late fee assessed")]
    public async Task Team_DepositPaid_NoLateFee_Owing_LateAssessed()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamRealize());

        team.FeeLatefee.Should().Be(LateFee, "a still-owing team that has paid no late fee is assessed the active one at charge");
        team.FeeBase.Should().Be(Deposit, "still deposit phase — only the late fee is added, FeeBase unchanged");
        team.OwedTotal.Should().Be(LateFee, "100 deposit + 50 late − 100 paid = 50 late now owed");
    }

    [Fact(DisplayName = "Charge realize: unpaid team in deposit phase → late fee assessed on top of deposit owed")]
    public async Task Team_Unpaid_LateAssessed()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: 0m, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamRealize());

        team.FeeLatefee.Should().Be(LateFee);
        team.OwedTotal.Should().Be(Deposit + LateFee, "100 deposit + 50 late − 0 paid");
    }

    [Fact(DisplayName = "Charge realize: active late fee already stamped (not yet paid) → re-derived to the same amount, never doubled")]
    public async Task Team_ExistingLateFee_NotDoubled()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: LateFee);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamRealize());

        team.FeeLatefee.Should().Be(LateFee, "the active late fee re-derives to the same amount — never stacked or doubled");
    }

    [Fact(DisplayName = "Charge realize: paid-in-full team gets NO late fee")]
    public async Task Team_PaidInFull_NoLateFee()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: FullPrice, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamRealize());

        team.FeeLatefee.Should().Be(0m, "a team that owes nothing against full price is never assessed a late fee");
        team.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Team swap WITHOUT the flag freezes the late fee (reprice / roster-pool path unchanged)")]
    public async Task Team_FlagOff_LateFeeFrozen()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId,
            new TeamFeeApplicationContext { IsFullPaymentRequired = false, AddProcessingFees = false, ProcessingFeePercent = 0m });

        team.FeeLatefee.Should().Be(0m, "the default path (a director reprice, or a roster/pool swap) keeps the late fee frozen");
    }

    // ── Player path: ApplySwapFeesAsync (same rule, symmetric) ──

    [Fact(DisplayName = "Charge realize: deposit-paid reg with no late fee + still owing → late fee assessed")]
    public async Task Player_DepositPaid_NoLateFee_Owing_LateAssessed()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, PlayerRealize());

        reg.FeeLatefee.Should().Be(LateFee, "a still-owing player reg that has paid no late fee is assessed the active one at charge");
        reg.FeeBase.Should().Be(Deposit, "player deposit-phase FeeBase = deposit; only late fee added");
        reg.OwedTotal.Should().Be(LateFee, "100 deposit + 50 late − 100 paid");
    }

    [Fact(DisplayName = "Charge realize: paid-in-full reg gets NO late fee")]
    public async Task Player_PaidInFull_NoLateFee()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: FullPrice, existingLateFee: 0m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, PlayerRealize());

        reg.FeeLatefee.Should().Be(0m);
    }

    [Fact(DisplayName = "Player swap WITHOUT the flag freezes the late fee (reprice / roster-pool path unchanged)")]
    public async Task Player_FlagOff_LateFeeFrozen()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId,
            new FeeApplicationContext { IsFullPaymentRequired = false, AddProcessingFees = false });

        reg.FeeLatefee.Should().Be(0m);
    }

    // ── Arrange helpers ──

    // The charge-entry realize context: AssessActiveLateFee = true, exactly as
    // RealizeLateFeeAtChargeAsync builds it. (Named for what sets it now — payment, not a reprice.)
    private static TeamFeeApplicationContext TeamRealize() =>
        new() { IsFullPaymentRequired = false, AddProcessingFees = false, ProcessingFeePercent = 0m, AssessActiveLateFee = true };

    private static FeeApplicationContext PlayerRealize() =>
        new() { IsFullPaymentRequired = false, AddProcessingFees = false, AssessActiveLateFee = true };

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
