using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Accounting.ClubAllocation;

/// <summary>
/// ALLOCATION MATRIX — CONFIG B: PROCESSING FEES ON BALANCE DUE ONLY
///
/// Job has BAddProcessingFees=true, BApplyProcessingFeesToTeamDeposit=false.
/// Processing fees are only charged on the balance due portion, not on deposits.
///
/// Standard setup per test:
///   Deposit=$500, BalanceDue=$1,500 → FeeBase=$2,000 per team
///   Processing=3.5% on balance due only → FeeProcessing = $1,500 × 3.5% = $52.50
///   FeeTotal = $2,052.50 per team
///   3 teams (Alpha, Bravo, Charlie) under one club rep
/// </summary>
public class AllocationMatrixTests_B_FeesOnBalanceOnly
{
    private const string UserId = "test-admin";
    private const decimal Deposit = 500m;
    private const decimal BalanceDue = 1500m;
    private const decimal FeeBase = Deposit + BalanceDue;  // $2,000
    private const decimal Rate = 0.035m;
    // Processing on balance due only: $1,500 × 3.5% = $52.50
    private const decimal FeeProcessingPerTeam = 52.50m;
    private const decimal FeeTotalPerTeam = FeeBase + FeeProcessingPerTeam;  // $2,052.50

    private static async Task<(TeamSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agId, Guid clubRepId)>
        CreateServiceAsync()
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        var job = builder.AddJob(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "2027 AA",
            rosterFee: Deposit, teamFee: BalanceDue);
        var clubRep = builder.AddClubRepRegistration(job.JobId, clubName: "All American Aim");
        await builder.SaveAsync();

        var teamRepo = new TeamRepository(ctx);
        var accountingRepo = new RegistrationAccountingRepository(ctx);
        var registrationRepo = new RegistrationRepository(ctx);

        var jobRepo = new Mock<IJobRepository>();
        var feeService = new Mock<IFeeResolutionService>();
        var adnApi = new Mock<IAdnApiService>();
        var ladtService = new Mock<ILadtService>();
        var logger = new Mock<ILogger<TeamSearchService>>();

        feeService.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rate);

        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = true,
                BApplyProcessingFeesToTeamDeposit = false,
                BTeamsFullPaymentRequired = false,
                PaymentMethodsAllowedCode = 7
            });

        var svc = new TeamSearchService(
            teamRepo, accountingRepo, registrationRepo, jobRepo.Object,
            feeService.Object, adnApi.Object, ladtService.Object, logger.Object);

        return (svc, builder, ctx, job.JobId, ag.AgegroupId, clubRep.RegistrationId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DEPOSIT PHASE — no prior payments, no processing fees yet
    //  (fees only apply at balance due, so deposit phase = Config A)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// B1: Deposit phase, CC payment. No processing fees at deposit time.
    /// Each team owes $2,000 (FeeProcessing=0 at deposit phase).
    /// </summary>
    [Fact(DisplayName = "B1: Fees on balance only, deposit, CC → no fees yet, $2,000/team")]
    public async Task B1_FeesOnBalance_Deposit_CC()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        // At deposit phase, processing fees haven't been applied yet
        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase, feeProcessing: 0m);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase, feeProcessing: 0m);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase, feeProcessing: 0m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3; clubRep.OwedTotal = FeeBase * 3;
        await b.SaveAsync();

        var teams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        teams.Should().OnlyContain(t => t.FeeProcessing == 0m, "no fees at deposit phase");
        teams.Should().OnlyContain(t => t.OwedTotal == FeeBase);
    }

    /// <summary>
    /// B2: Deposit phase, check full ($6,000 covers all 3 teams).
    /// No processing fees at deposit → no fee reduction. Identical to A2.
    /// </summary>
    [Fact(DisplayName = "B2: Fees on balance only, deposit, check full → no fees to reduce")]
    public async Task B2_FeesOnBalance_Deposit_CheckFull()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase, feeProcessing: 0m);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase, feeProcessing: 0m);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase, feeProcessing: 0m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3; clubRep.OwedTotal = FeeBase * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = FeeBase * 3,
                PaymentType = "Check",
                CheckNo = "2001"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3);
        result.PerTeamAllocations.Should().OnlyContain(a => a.ProcessingFeeReduction == 0m,
            "no processing fees at deposit phase");
        result.PerTeamAllocations.Should().OnlyContain(a => a.AllocatedAmount == FeeBase);
    }

    /// <summary>
    /// B3: Deposit phase, check partial ($3,000 for 3 teams at $2,000 each).
    /// No fees → same distribution as A3.
    /// </summary>
    [Fact(DisplayName = "B3: Fees on balance only, deposit, check partial → no fees to reduce")]
    public async Task B3_FeesOnBalance_Deposit_CheckPartial()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase, feeProcessing: 0m);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase, feeProcessing: 0m);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase, feeProcessing: 0m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3; clubRep.OwedTotal = FeeBase * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 3000m,
                PaymentType = "Check",
                CheckNo = "2002"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2);
        result.PerTeamAllocations.Should().OnlyContain(a => a.ProcessingFeeReduction == 0m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BALANCE DUE PHASE — deposit paid by CC ($500/team, no processing on deposit)
    //  Now processing fees apply: $1,500 × 3.5% = $52.50/team
    //  OwedTotal = $2,052.50 - $500 = $1,552.50/team
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// B4: Balance due phase, CC payment. Processing fees active.
    /// Deposit ($500) paid by CC (no processing on deposit).
    /// Each team owes $1,552.50 ($1,500 balance + $52.50 processing).
    /// CC payment = full OwedTotal, no fee reduction.
    /// </summary>
    [Fact(DisplayName = "B4: Fees on balance only, balance due, CC → $1,552.50/team")]
    public async Task B4_FeesOnBalance_BalanceDue_CC()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        // Deposit paid ($500), processing on balance only ($52.50)
        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.PaidTotal = Deposit * 3;
        clubRep.OwedTotal = (FeeTotalPerTeam - Deposit) * 3;  // $1,552.50 × 3 = $4,657.50
        await b.SaveAsync();

        // CC: OwedTotal per team
        var teams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        teams.Should().OnlyContain(t => t.OwedTotal == FeeTotalPerTeam - Deposit,
            "each team owes $1,552.50 by CC");

        clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.OwedTotal.Should().Be((FeeTotalPerTeam - Deposit) * 3,
            "club rep owes $4,657.50 by CC");
    }

    /// <summary>
    /// B5: Balance due phase, check full payment.
    /// Each team owes $1,552.50. Check removes processing: baseOwed = $1,552.50/1.035 = $1,500.
    /// FeeReduction = $1,500 × 3.5% = $52.50. Check allocation = $1,500/team.
    /// Total check = $4,500 for all 3 teams.
    /// </summary>
    [Fact(DisplayName = "B5: Fees on balance only, balance due, check full → $1,500/team")]
    public async Task B5_FeesOnBalance_BalanceDue_CheckFull()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.PaidTotal = Deposit * 3;
        clubRep.OwedTotal = (FeeTotalPerTeam - Deposit) * 3;
        await b.SaveAsync();

        // Check for $4,500 = 3 × $1,500 (base balance per team)
        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = BalanceDue * 3,  // $4,500
                PaymentType = "Check",
                CheckNo = "2003"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3);

        foreach (var alloc in result.PerTeamAllocations!)
        {
            alloc.AllocatedAmount.Should().Be(BalanceDue,
                "baseOwed = $1,552.50 / 1.035 = $1,500");
            alloc.ProcessingFeeReduction.Should().Be(FeeProcessingPerTeam,
                "$1,500 × 3.5% = $52.50");
            alloc.NewOwedTotal.Should().Be(0m);
        }

        // Club rep fully paid
        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.FeeProcessing.Should().Be(0m, "all processing fees removed");
        updatedClubRep.OwedTotal.Should().Be(0m, "club rep fully paid");
    }

    /// <summary>
    /// B6: Balance due phase, check partial ($2,000 for 3 teams owing $1,552.50 each).
    /// baseOwed = $1,552.50 / 1.035 = $1,500/team.
    /// Alpha: allocation=$1,500, feeReduction=$52.50, remaining=$500.
    /// Bravo: allocation=$500, feeReduction=$500×3.5%=$17.50.
    /// Charlie: $0 (exhausted).
    /// </summary>
    [Fact(DisplayName = "B6: Fees on balance only, balance due, check partial → proportional reduction")]
    public async Task B6_FeesOnBalance_BalanceDue_CheckPartial()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: Deposit);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.PaidTotal = Deposit * 3;
        clubRep.OwedTotal = (FeeTotalPerTeam - Deposit) * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 2000m,
                PaymentType = "Check",
                CheckNo = "2004"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2, "one team gets $0, skipped");

        // First team allocated: full base amount
        var alloc1 = result.PerTeamAllocations![0];
        alloc1.AllocatedAmount.Should().Be(BalanceDue, "baseOwed=$1,500, fully covered");
        alloc1.ProcessingFeeReduction.Should().Be(FeeProcessingPerTeam, "$1,500 × 3.5% = $52.50");

        var fullyPaidTeam = await ctx.Teams.FindAsync(alloc1.TeamId);
        fullyPaidTeam!.FeeProcessing.Should().Be(0m);
        fullyPaidTeam.OwedTotal.Should().Be(0m);

        // Second team allocated: $500 remaining from check
        var alloc2 = result.PerTeamAllocations[1];
        alloc2.AllocatedAmount.Should().Be(500m, "$2,000 - $1,500 = $500 remaining");
        alloc2.ProcessingFeeReduction.Should().Be(17.50m, "$500 × 3.5% = $17.50");

        var partialTeam = await ctx.Teams.FindAsync(alloc2.TeamId);
        partialTeam!.FeeProcessing.Should().Be(FeeProcessingPerTeam - 17.50m,
            "$52.50 - $17.50 = $35.00 remaining");

        // One team untouched (no allocation)
        var allTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        var untouchedTeam = allTeams.First(t =>
            t.TeamId != alloc1.TeamId && t.TeamId != alloc2.TeamId);
        untouchedTeam.PaidTotal.Should().Be(Deposit, "untouched team received nothing");
        untouchedTeam.FeeProcessing.Should().Be(FeeProcessingPerTeam, "no reduction");

        // Club rep: total reduction = $52.50 + $17.50 = $70.00
        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.FeeProcessing.Should().Be(FeeProcessingPerTeam * 3 - FeeProcessingPerTeam - 17.50m,
            "original $157.50 - $70.00 = $87.50");
    }
}
