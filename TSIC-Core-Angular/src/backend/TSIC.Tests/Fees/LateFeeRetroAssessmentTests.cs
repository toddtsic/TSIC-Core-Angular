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
/// Retroactive late-fee assessment in the swap/reprice appliers, gated by the
/// <c>AssessActiveLateFee</c> context flag (set ONLY by the reprice engines —
/// RecalculateTeamFeesAsync / RecalculatePlayerFeesAsync, the director's "update all
/// prior" action). Without the flag every modifier stays frozen (covered by
/// <see cref="PlayerRegistration.EarlyBirdLateFee.EarlyBirdLateFeeTests"/>).
///
/// Product rule: a reprice may stamp a currently-active late fee onto a record that
///   (a) carries NO late fee yet, AND
///   (b) still owes principal against the FULL price (not paid in full).
/// It never doubles an existing late fee, never touches a paid-in-full record, and
/// never disturbs a frozen discount/donation — it only ever ADDS a late fee where none
/// exists. This is the gap Ann reported: a team that registered + paid its deposit before
/// the director set a late fee never saw that fee at balance time.
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

    [Fact(DisplayName = "Team reprice: deposit-paid team with no late fee + still owing → late fee assessed")]
    public async Task Team_DepositPaid_NoLateFee_Owing_LateAssessed()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamReprice());

        team.FeeLatefee.Should().Be(LateFee, "a still-owing team with no late fee is retroactively assessed");
        team.FeeBase.Should().Be(Deposit, "still deposit phase — only the late fee is added, FeeBase unchanged");
        team.OwedTotal.Should().Be(LateFee, "100 deposit + 50 late − 100 paid = 50 late now owed");
    }

    [Fact(DisplayName = "Team reprice: unpaid team in deposit phase → late fee assessed on top of deposit owed")]
    public async Task Team_Unpaid_LateAssessed()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: 0m, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamReprice());

        team.FeeLatefee.Should().Be(LateFee);
        team.OwedTotal.Should().Be(Deposit + LateFee, "100 deposit + 50 late − 0 paid");
    }

    [Fact(DisplayName = "Team reprice: team that already carries a late fee is NOT doubled")]
    public async Task Team_ExistingLateFee_NotDoubled()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: LateFee);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamReprice());

        team.FeeLatefee.Should().Be(LateFee, "an existing late fee is frozen — never re-stamped or doubled");
    }

    [Fact(DisplayName = "Team reprice: paid-in-full team gets NO late fee")]
    public async Task Team_PaidInFull_NoLateFee()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: FullPrice, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId, TeamReprice());

        team.FeeLatefee.Should().Be(0m, "a team that owes nothing against full price is never assessed a late fee");
        team.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Team swap WITHOUT the flag freezes the late fee (default path unchanged)")]
    public async Task Team_FlagOff_LateFeeFrozen()
    {
        var (svc, team, jobId, agId) = await ArrangeTeamAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplyTeamSwapFeesAsync(team, jobId, agId,
            new TeamFeeApplicationContext { IsFullPaymentRequired = false, AddProcessingFees = false, ProcessingFeePercent = 0m });

        team.FeeLatefee.Should().Be(0m, "the default (roster/pool swap) path keeps all modifiers frozen");
    }

    // ── Player path: ApplySwapFeesAsync (same rule, symmetric) ──

    [Fact(DisplayName = "Player reprice: deposit-paid reg with no late fee + still owing → late fee assessed")]
    public async Task Player_DepositPaid_NoLateFee_Owing_LateAssessed()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, PlayerReprice());

        reg.FeeLatefee.Should().Be(LateFee, "a still-owing player reg with no late fee is retroactively assessed");
        reg.FeeBase.Should().Be(Deposit, "player deposit-phase FeeBase = deposit; only late fee added");
        reg.OwedTotal.Should().Be(LateFee, "100 deposit + 50 late − 100 paid");
    }

    [Fact(DisplayName = "Player reprice: paid-in-full reg gets NO late fee")]
    public async Task Player_PaidInFull_NoLateFee()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: FullPrice, existingLateFee: 0m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId, PlayerReprice());

        reg.FeeLatefee.Should().Be(0m);
    }

    [Fact(DisplayName = "Player swap WITHOUT the flag freezes the late fee (default path unchanged)")]
    public async Task Player_FlagOff_LateFeeFrozen()
    {
        var (svc, reg, jobId, agId, teamId) = await ArrangePlayerAsync(paid: Deposit, existingLateFee: 0m);

        await svc.ApplySwapFeesAsync(reg, jobId, agId, teamId,
            new FeeApplicationContext { IsFullPaymentRequired = false, AddProcessingFees = false });

        reg.FeeLatefee.Should().Be(0m);
    }

    // ── Arrange helpers ──

    private static TeamFeeApplicationContext TeamReprice() =>
        new() { IsFullPaymentRequired = false, AddProcessingFees = false, ProcessingFeePercent = 0m, AssessActiveLateFee = true };

    private static FeeApplicationContext PlayerReprice() =>
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
