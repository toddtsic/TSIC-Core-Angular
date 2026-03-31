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
/// ALLOCATION MATRIX — CONFIG C: PROCESSING FEES ON BOTH DEPOSIT AND BALANCE DUE
///
/// Job has BAddProcessingFees=true, BApplyProcessingFeesToTeamDeposit=true.
/// Processing fees apply to the full amount ($2,000 × 3.5% = $70).
///
/// Standard setup per test:
///   Deposit=$500, BalanceDue=$1,500 → FeeBase=$2,000 per team
///   Processing=3.5% on full amount → FeeProcessing=$70, FeeTotal=$2,070
///
///   Deposit phase:  Nothing paid. OwedTotal = $2,070.
///   Balance due phase:
///     Deposit paid by CC: $500 + $17.50 processing = $517.50
///     Balance owed: $1,500 + $52.50 processing on balance = $1,552.50
///
///   3 teams (Alpha, Bravo, Charlie) under one club rep
/// </summary>
public class AllocationMatrixTests_C_FeesOnBoth
{
    private const string UserId = "test-admin";
    private const decimal Deposit = 500m;
    private const decimal BalanceDue = 1500m;
    private const decimal FeeBase = Deposit + BalanceDue;                  // $2,000
    private const decimal Rate = 0.035m;
    private const decimal FeeProcessingPerTeam = FeeBase * Rate;           // $70.00
    private const decimal FeeTotalPerTeam = FeeBase + FeeProcessingPerTeam; // $2,070.00

    // Deposit phase
    private const decimal DepositProcessing = Deposit * Rate;              // $17.50
    private const decimal DepositCcPayment = Deposit + DepositProcessing;  // $517.50

    // Balance due phase — what remains after deposit paid by CC
    private const decimal BalanceProcessing = BalanceDue * Rate;           // $52.50
    private const decimal BalanceDueOwed = BalanceDue + BalanceProcessing; // $1,552.50

    private static async Task<(TeamSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agId, Guid clubRepId)>
        CreateServiceAsync()
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        var job = builder.AddJob(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);
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
                BApplyProcessingFeesToTeamDeposit = true,
                BTeamsFullPaymentRequired = false,
                PaymentMethodsAllowedCode = 7
            });

        var svc = new TeamSearchService(
            teamRepo, accountingRepo, registrationRepo, jobRepo.Object,
            feeService.Object, adnApi.Object, ladtService.Object, logger.Object);

        return (svc, builder, ctx, job.JobId, ag.AgegroupId, clubRep.RegistrationId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DEPOSIT PHASE — nothing paid yet
    //  OwedTotal = $2,070 ($2,000 base + $70 processing)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// C1: Deposit phase, CC payment. Full processing fees active.
    /// Each team owes $2,070. CC pays $517.50 deposit ($500 + $17.50 processing).
    /// No fee reduction for CC payments.
    /// </summary>
    [Fact(DisplayName = "C1: Fees on both, deposit, CC → $2,070/team owed")]
    public async Task C1_FeesOnBoth_Deposit_CC()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.OwedTotal = FeeTotalPerTeam * 3;
        await b.SaveAsync();

        var teams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        teams.Should().OnlyContain(t => t.OwedTotal == FeeTotalPerTeam,
            "each team owes $2,070 by CC");
        teams.Should().OnlyContain(t => t.FeeProcessing == FeeProcessingPerTeam);

        clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.OwedTotal.Should().Be(FeeTotalPerTeam * 3, "club rep owes $6,210 by CC");
    }

    /// <summary>
    /// C2: Deposit phase, check full ($6,000 = 3 × $2,000 base).
    /// baseOwed = $2,070 / 1.035 = $2,000. Allocation = $2,000/team.
    /// FeeReduction = $2,000 × 3.5% = $70. All processing removed.
    /// </summary>
    [Fact(DisplayName = "C2: Fees on both, deposit, check full → $2,000/team, $70 reduction each")]
    public async Task C2_FeesOnBoth_Deposit_CheckFull()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.OwedTotal = FeeTotalPerTeam * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = FeeBase * 3,  // $6,000 — the base amount for all teams
                PaymentType = "Check",
                CheckNo = "3001"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3);

        foreach (var alloc in result.PerTeamAllocations!)
        {
            alloc.AllocatedAmount.Should().Be(FeeBase,
                "baseOwed = $2,070 / 1.035 = $2,000");
            alloc.ProcessingFeeReduction.Should().Be(FeeProcessingPerTeam,
                "$2,000 × 3.5% = $70");
            alloc.NewOwedTotal.Should().Be(0m);
        }

        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.FeeProcessing.Should().Be(0m);
        updatedClubRep.OwedTotal.Should().Be(0m);
    }

    /// <summary>
    /// C3: Deposit phase, check partial ($3,000 for 3 teams owing $2,070 each).
    /// baseOwed = $2,000/team. Alpha gets $2,000, Bravo gets $1,000, Charlie $0.
    /// Fee reductions: Alpha=$70, Bravo=$1,000×3.5%=$35.
    /// </summary>
    [Fact(DisplayName = "C3: Fees on both, deposit, check partial → proportional reduction")]
    public async Task C3_FeesOnBoth_Deposit_CheckPartial()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.OwedTotal = FeeTotalPerTeam * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 3000m,
                PaymentType = "Check",
                CheckNo = "3002"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2, "one team gets $0, skipped");

        // First team: full base allocation
        var alloc1 = result.PerTeamAllocations![0];
        alloc1.AllocatedAmount.Should().Be(FeeBase, "baseOwed=$2,000, fully covered");
        alloc1.ProcessingFeeReduction.Should().Be(FeeProcessingPerTeam, "$2,000 × 3.5% = $70");

        var fullyPaidTeam = await ctx.Teams.FindAsync(alloc1.TeamId);
        fullyPaidTeam!.FeeProcessing.Should().Be(0m);
        fullyPaidTeam.OwedTotal.Should().Be(0m);

        // Second team: $1,000 remaining
        var alloc2 = result.PerTeamAllocations[1];
        alloc2.AllocatedAmount.Should().Be(1000m, "$3,000 - $2,000 = $1,000 remaining");
        alloc2.ProcessingFeeReduction.Should().Be(35m, "$1,000 × 3.5% = $35");

        var partialTeam = await ctx.Teams.FindAsync(alloc2.TeamId);
        partialTeam!.FeeProcessing.Should().Be(FeeProcessingPerTeam - 35m, "$70 - $35 = $35 remaining");

        // Untouched team
        var allTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        var untouchedTeam = allTeams.First(t =>
            t.TeamId != alloc1.TeamId && t.TeamId != alloc2.TeamId);
        untouchedTeam.PaidTotal.Should().Be(0m);
        untouchedTeam.FeeProcessing.Should().Be(FeeProcessingPerTeam);

        // Club rep
        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.FeeProcessing.Should().Be(FeeProcessingPerTeam * 3 - FeeProcessingPerTeam - 35m,
            "total reduction: $70 + $35 = $105; original $210 - $105 = $105");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BALANCE DUE PHASE — deposit paid by CC
    //  Deposit paid: $500 base + $17.50 processing = $517.50/team
    //  Balance owed: $1,500 + $52.50 processing on balance = $1,552.50/team
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// C4: Balance due phase, CC payment.
    /// Deposit ($517.50) paid by CC. Balance owed = $1,500 + $52.50 = $1,552.50/team.
    /// CC pays full OwedTotal, no fee reduction.
    /// </summary>
    [Fact(DisplayName = "C4: Fees on both, balance due, CC → $1,552.50/team")]
    public async Task C4_FeesOnBoth_BalanceDue_CC()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        // Deposit paid by CC ($517.50), remaining processing = $52.50
        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.PaidTotal = DepositCcPayment * 3;
        clubRep.OwedTotal = BalanceDueOwed * 3;  // $1,552.50 × 3 = $4,657.50
        await b.SaveAsync();

        // CC: balance owed per team = $1,500 + $52.50 = $1,552.50
        var teams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        teams.Should().OnlyContain(t => t.OwedTotal == BalanceDueOwed,
            "each team owes $1,552.50 by CC");

        clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.OwedTotal.Should().Be(BalanceDueOwed * 3, "club rep owes $4,657.50 by CC");
    }

    /// <summary>
    /// C5: Balance due phase, check full payment. ANN'S SCENARIO.
    /// Each team owes $1,552.50 ($1,500 balance + $52.50 processing).
    /// baseOwed = $1,552.50 / 1.035 = $1,500. Check = $4,500 (3 × $1,500).
    /// FeeReduction = $1,500 × 3.5% = $52.50/team. The $17.50 from the CC deposit stays.
    /// Verification: $52.50 + $17.50 = $70.00 = total FeeProcessing per team ✓
    /// </summary>
    [Fact(DisplayName = "C5: Fees on both, balance due, check full → $1,500/team (Ann's scenario)")]
    public async Task C5_FeesOnBoth_BalanceDue_CheckFull()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.PaidTotal = DepositCcPayment * 3;
        clubRep.OwedTotal = BalanceDueOwed * 3;
        await b.SaveAsync();

        // Check for $4,500 = 3 × $1,500 (base balance per team)
        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = BalanceDue * 3,  // $4,500
                PaymentType = "Check",
                CheckNo = "3003"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3);

        foreach (var alloc in result.PerTeamAllocations!)
        {
            alloc.AllocatedAmount.Should().Be(BalanceDue,
                "baseOwed = $1,552.50 / 1.035 = $1,500");
            alloc.ProcessingFeeReduction.Should().Be(BalanceProcessing,
                "$1,500 × 3.5% = $52.50 — only the balance-due processing removed");
            alloc.NewOwedTotal.Should().Be(0m);
        }

        // Each team: FeeProcessing was $70, reduced by $52.50 → $17.50 remaining
        // The $17.50 is the processing from the CC deposit — correctly preserved
        var teams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        teams.Should().OnlyContain(t => t.FeeProcessing == DepositProcessing,
            "$70 - $52.50 = $17.50 — processing from CC deposit preserved");
        teams.Should().OnlyContain(t => t.OwedTotal == 0m);

        // Club rep
        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.FeeProcessing.Should().Be(DepositProcessing * 3,
            "$17.50 × 3 = $52.50 — only CC deposit processing remains");
        updatedClubRep.OwedTotal.Should().Be(0m);
    }

    /// <summary>
    /// C6: Balance due phase, check partial ($2,000 for 3 teams owing $1,552.50 each).
    /// baseOwed = $1,552.50 / 1.035 = $1,500/team.
    /// Alpha: allocation=$1,500, feeReduction=$52.50. Remaining=$500.
    /// Bravo: allocation=$500, feeReduction=$500×3.5%=$17.50.
    /// Charlie: $0 (exhausted).
    /// </summary>
    [Fact(DisplayName = "C6: Fees on both, balance due, check partial → proportional reduction")]
    public async Task C6_FeesOnBoth_BalanceDue_CheckPartial()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: FeeBase, feeProcessing: FeeProcessingPerTeam, paidTotal: DepositCcPayment);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeProcessing = FeeProcessingPerTeam * 3;
        clubRep.FeeTotal = FeeTotalPerTeam * 3; clubRep.PaidTotal = DepositCcPayment * 3;
        clubRep.OwedTotal = BalanceDueOwed * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 2000m,
                PaymentType = "Check",
                CheckNo = "3004"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2, "one team gets $0, skipped");

        // First team: full base balance allocation
        var alloc1 = result.PerTeamAllocations![0];
        alloc1.AllocatedAmount.Should().Be(BalanceDue, "baseOwed=$1,500, fully covered");
        alloc1.ProcessingFeeReduction.Should().Be(BalanceProcessing,
            "$1,500 × 3.5% = $52.50");

        var fullyPaidTeam = await ctx.Teams.FindAsync(alloc1.TeamId);
        fullyPaidTeam!.FeeProcessing.Should().Be(DepositProcessing,
            "$70 - $52.50 = $17.50 — CC deposit processing preserved");
        fullyPaidTeam.OwedTotal.Should().Be(0m);

        // Second team: $500 remaining from check
        var alloc2 = result.PerTeamAllocations[1];
        alloc2.AllocatedAmount.Should().Be(500m, "$2,000 - $1,500 = $500 remaining");
        alloc2.ProcessingFeeReduction.Should().Be(17.50m, "$500 × 3.5% = $17.50");

        var partialTeam = await ctx.Teams.FindAsync(alloc2.TeamId);
        partialTeam!.FeeProcessing.Should().Be(FeeProcessingPerTeam - 17.50m,
            "$70 - $17.50 = $52.50 remaining");

        // Untouched team
        var allTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        var untouchedTeam = allTeams.First(t =>
            t.TeamId != alloc1.TeamId && t.TeamId != alloc2.TeamId);
        untouchedTeam.PaidTotal.Should().Be(DepositCcPayment, "untouched team received nothing new");
        untouchedTeam.FeeProcessing.Should().Be(FeeProcessingPerTeam, "no reduction");
        untouchedTeam.OwedTotal.Should().Be(BalanceDueOwed, "still owes $1,552.50");

        // Club rep: total reduction = $52.50 + $17.50 = $70
        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.FeeProcessing.Should().Be(FeeProcessingPerTeam * 3 - BalanceProcessing - 17.50m,
            "original $210 - $52.50 - $17.50 = $140");
    }
}
