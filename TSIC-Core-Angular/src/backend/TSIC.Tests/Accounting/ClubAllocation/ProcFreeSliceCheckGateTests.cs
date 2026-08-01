using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Accounting.ClubAllocation;

/// <summary>
/// POLICY-B CHECK GATE — PROC-FREE DEPOSIT SLICE (integration)
///
/// Job class: BAddProcessingFees=1, BApplyProcessingFeesToTeamDeposit=0. The deposit slice
/// of a team's bill carries NO proc, so a check dollar landing inside the still-unpaid
/// deposit slice displaces no proc — only dollars beyond FreeSliceRemaining decrement
/// FeeProcessing at the CC rate (Todd's ruling "B": the deposit comes off the calculation).
///
/// Unlike AllocationMatrixTests_B (which seeds no JobFees, so hydration resolves no slice
/// and the gate is inert), this arrange seeds a league-level ClubRep JobFees row —
/// PaymentStateService resolves the 500 deposit itself (Step 7 centralization: zero caller
/// wiring) and the gate goes LIVE end-to-end through RecordCheckForClubAsync.
///
/// Fixture (the Wildcat shape): Deposit=$500, BalanceDue=$1,500 → FeeBase=$2,000;
/// proc 3.5% on balance only → FeeProcessing=$52.50, FeeTotal=$2,052.50.
/// </summary>
public class ProcFreeSliceCheckGateTests
{
    private const string UserId = "test-admin";
    private const decimal Deposit = 500m;
    private const decimal BalanceDue = 1500m;
    private const decimal FeeBase = Deposit + BalanceDue;          // $2,000
    private const decimal Rate = 0.035m;
    private const decimal FeeProcessingPerTeam = 52.50m;           // 1,500 × 3.5%
    private const decimal FeeTotalPerTeam = FeeBase + FeeProcessingPerTeam; // $2,052.50

    private static async Task<(TeamSearchService svc,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid teamId, Guid clubRepId,
        PaymentStateService paymentState)>
        CreateServiceAsync()
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var league = acct.AddLeague(job.JobId);
        var ag = acct.AddAgegroup(league.LeagueId, "2027 AA");
        var clubRep = acct.AddClubRepRegistration(job.JobId, clubName: "Wildcat LC");

        // The slice source: league-level ClubRep fee — hydration resolves EffectiveDeposit
        // 500 from this row via the real FeeRepository cascade. No caller wiring anywhere.
        fees.AddJobFee(job.JobId, RoleConstants.ClubRep, leagueId: league.LeagueId,
            deposit: Deposit, balanceDue: BalanceDue);

        var team = acct.AddTeam(job.JobId, ag.AgegroupId, clubRep.RegistrationId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);

        clubRep.FeeBase = FeeBase; clubRep.FeeProcessing = FeeProcessingPerTeam;
        clubRep.FeeTotal = FeeTotalPerTeam; clubRep.OwedTotal = FeeTotalPerTeam;
        await acct.SaveAsync();

        var teamRepo = new TeamRepository(ctx);
        var accountingRepo = new RegistrationAccountingRepository(ctx);
        var registrationRepo = new RegistrationRepository(ctx);

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = true,
                BApplyProcessingFeesToTeamDeposit = false,
                PaymentMethodsAllowedCode = 7
            });
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        jobRepo.Setup(j => j.GetEcprocessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1.5m);

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rate);

        var paymentState = new PaymentStateService(accountingRepo, jobRepo.Object, new FeeRepository(ctx), new TeamRepository(ctx));
        var svc = new TeamSearchService(
            teamRepo, accountingRepo, registrationRepo, jobRepo.Object,
            feeService.Object, paymentState, new Mock<IAdnApiService>().Object,
            new Mock<ILadtService>().Object,
            new Mock<IEmailService>().Object, new Mock<IPaymentService>().Object,
            new Mock<TSIC.API.Services.Teams.IRegisteredTeamShaper>().Object,
            new Mock<TSIC.API.Services.Teams.ITeamRenameService>().Object,
            new Mock<ILogger<TeamSearchService>>().Object);

        return (svc, ctx, job.JobId, team.TeamId, clubRep.RegistrationId, paymentState);
    }

    private static TeamCheckOrCorrectionRequest Check(Guid clubRepId, decimal amount, string no) =>
        new()
        {
            ClubRepRegistrationId = clubRepId,
            Amount = amount,
            PaymentType = "Check",
            CheckNo = no
        };

    /// <summary>
    /// A $500 check on an untouched team lands entirely inside the unpaid deposit slice —
    /// that slice never carried proc, so NOTHING is displaced. The pre-fix behavior credited
    /// 500 × 3.5% = $17.50 the customer was never going to be charged.
    /// </summary>
    [Fact(DisplayName = "Slice gate: $500 check inside the deposit slice → zero proc reduction")]
    public async Task Check_InsideDepositSlice_NoProcReduction()
    {
        var (svc, ctx, jobId, teamId, clubRepId, _) = await CreateServiceAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId, Check(clubRepId, Deposit, "3001"));

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().ContainSingle()
            .Which.ProcessingFeeReduction.Should().Be(0m, "the deposit slice never carried proc");

        var team = await ctx.Teams.FirstAsync(t => t.TeamId == teamId);
        team.FeeProcessing.Should().Be(FeeProcessingPerTeam, "balance-slice proc is untouched");
        team.PaidTotal.Should().Be(Deposit);
        team.OwedTotal.Should().Be(FeeTotalPerTeam - Deposit); // 1,552.50 — balance + its proc
    }

    /// <summary>
    /// One $2,000 check covering the whole principal: the first $500 fills the proc-free
    /// slice (displaces nothing), only the $1,500 spillover displaces proc → reduction
    /// exactly $52.50, team settles at owed 0. Pre-fix the naive amount × rate credited
    /// $70.00 and drove FeeProcessing negative-of-truth by $17.50.
    /// </summary>
    [Fact(DisplayName = "Slice gate: $2,000 check spanning slice + balance → reduction only on the spillover")]
    public async Task Check_SpansSliceAndBalance_ReducesOnlySpillover()
    {
        var (svc, ctx, jobId, teamId, clubRepId, _) = await CreateServiceAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId, Check(clubRepId, FeeBase, "3002"));

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().ContainSingle()
            .Which.ProcessingFeeReduction.Should().Be(BalanceDue * Rate); // 52.50, NOT 2,000 × 3.5% = 70

        var team = await ctx.Teams.FirstAsync(t => t.TeamId == teamId);
        team.FeeProcessing.Should().Be(0m);
        team.PaidTotal.Should().Be(FeeBase);
        team.OwedTotal.Should().Be(0m, "principal fully paid by check; no proc ever owed on it");
    }

    /// <summary>
    /// Sequential tenders: the deposit check consumes the slice; hydration for the SECOND
    /// check sees FreeSliceRemaining 0 from the ledger, so every follow-up dollar displaces
    /// at the full CC rate. Ends identical to the single-check path — order-independent.
    /// </summary>
    [Fact(DisplayName = "Slice gate: $500 check then $1,500 check → 0 then 52.50, settles at owed 0")]
    public async Task Check_DepositThenBalance_GateReopensAfterSliceFills()
    {
        var (svc, ctx, jobId, teamId, clubRepId, _) = await CreateServiceAsync();

        var first = await svc.RecordCheckForClubAsync(jobId, UserId, Check(clubRepId, Deposit, "3003"));
        first.Success.Should().BeTrue();
        first.PerTeamAllocations.Should().ContainSingle().Which.ProcessingFeeReduction.Should().Be(0m);

        var second = await svc.RecordCheckForClubAsync(jobId, UserId, Check(clubRepId, BalanceDue, "3004"));
        second.Success.Should().BeTrue();
        second.PerTeamAllocations.Should().ContainSingle()
            .Which.ProcessingFeeReduction.Should().Be(BalanceDue * Rate); // slice already filled

        var team = await ctx.Teams.FirstAsync(t => t.TeamId == teamId);
        team.FeeProcessing.Should().Be(0m);
        team.PaidTotal.Should().Be(FeeBase);
        team.OwedTotal.Should().Be(0m);
    }

    /// <summary>
    /// A CC deposit charge fully refunded (Credit Card Credit, negative Payamt): the walk
    /// attributes the +500 to the free slice, but the refund nets the bucket back to 0 —
    /// hydration must UNWIND the free attribution. Unclamped, CcProcFreeGross stayed 500
    /// against net gross 0 → phantom principal 16.91, ProcCollected −16.91, and the next
    /// reprice stamped proc 35.59 instead of 52.50 (silent undercharge).
    /// </summary>
    [Fact(DisplayName = "Slice hydration: full CC deposit refund unwinds free attribution — exact zeros, reprice holds 52.50")]
    public async Task Hydration_DepositRefunded_UnwindsFreeAttribution()
    {
        var (_, ctx, jobId, teamId, _, paymentState) = await CreateServiceAsync();

        // Rows added directly — a second AccountingDataBuilder would re-seed payment methods
        // on the shared ctx. Field shape mirrors AccountingDataBuilder.AddPayment.
        ctx.RegistrationAccounting.AddRange(
            new Domain.Entities.RegistrationAccounting
            {
                TeamId = teamId, Payamt = Deposit, Dueamt = Deposit,                 // proc-free deposit charge
                PaymentMethodId = AccountingDataBuilder.CcPaymentMethodId,
                Active = true, Createdate = new DateTime(2026, 1, 1), Modified = DateTime.UtcNow
            },
            new Domain.Entities.RegistrationAccounting
            {
                TeamId = teamId, Payamt = -Deposit, Dueamt = -Deposit,               // full refund
                PaymentMethodId = AccountingDataBuilder.CcCreditMethodId,
                Active = true, Createdate = new DateTime(2026, 1, 2), Modified = DateTime.UtcNow
            });
        await ctx.SaveChangesAsync();

        var state = await paymentState.ForTeamAsync(teamId, jobId);

        state.CcGrossPaid.Should().Be(0m);
        state.CcProcFreeGross.Should().Be(0m, "refunded dollars must not remain attributed to the free slice");
        state.CcPrincipalPaid.Should().Be(0m);
        state.CcProcCollected.Should().Be(0m);
        state.FreeSliceRemaining.Should().Be(Deposit, "the refunded deposit obligation is unpaid again");
        // Reprice on this ledger reproduces the booked proc exactly — not the 35.59 undercharge.
        state.FeeProcessingTarget(BalanceDue, 0m, 0m, 0m).Should().Be(FeeProcessingPerTeam);
    }
}
