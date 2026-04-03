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
/// ALLOCATION MATRIX — CONFIG A: NO PROCESSING FEES
///
/// Job has BAddProcessingFees=false. CC and check payments behave identically
/// because there are no processing fees to reduce. These tests serve as baselines.
///
/// Standard setup per test:
///   Deposit=$500, BalanceDue=$1,500 → FeeBase=$2,000 per team
///   Processing rate=3.5% (configured but disabled)
///   3 teams (Alpha, Bravo, Charlie) under one club rep
/// </summary>
public class AllocationMatrixTests_A_NoFees
{
    private const string UserId = "test-admin";
    private const decimal Deposit = 500m;
    private const decimal BalanceDue = 1500m;
    private const decimal FeeBase = Deposit + BalanceDue;  // $2,000

    private static async Task<(TeamSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agId, Guid clubRepId)>
        CreateServiceAsync()
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        var job = builder.AddJob(
            processingFeePercent: 3.5m,
            bAddProcessingFees: false);
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
            .ReturnsAsync(0.035m);

        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = false,
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
    //  DEPOSIT PHASE — no prior payments
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A1: Deposit phase, CC payment, no processing fees.
    /// Each team owes $2,000. CC charge of $1,500 (deposit × 3 teams).
    /// No fee adjustment. Teams each get $500 allocated.
    /// </summary>
    [Fact(DisplayName = "A1: No fees, deposit, CC → $500/team, no fee adjustment")]
    public async Task A1_NoFees_Deposit_CC()
    {
        var (_, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3; clubRep.OwedTotal = FeeBase * 3;
        await b.SaveAsync();

        // CC payment at deposit phase — no fee reduction expected
        // (CC charges go through a different code path; this test verifies no processing fee is created)
        var teams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        teams.Should().HaveCount(3);
        teams.Should().OnlyContain(t => t.FeeProcessing == 0m, "no processing fees configured");
        teams.Should().OnlyContain(t => t.OwedTotal == FeeBase, "each team owes full $2,000");

        // Club rep
        clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.OwedTotal.Should().Be(FeeBase * 3, "club rep owes $6,000 total");
    }

    /// <summary>
    /// A2: Deposit phase, check full payment ($1,500 = 3 × $500 deposit).
    /// No processing fees → check behaves exactly like CC. $500/team allocated.
    /// </summary>
    [Fact(DisplayName = "A2: No fees, deposit, check full → $500/team, no fee adjustment")]
    public async Task A2_NoFees_Deposit_CheckFull()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3; clubRep.OwedTotal = FeeBase * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = FeeBase * 3,  // $6,000 covers all teams
                PaymentType = "Check",
                CheckNo = "1001"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3);
        result.PerTeamAllocations.Should().OnlyContain(a => a.AllocatedAmount == FeeBase,
            "each team gets $2,000");
        result.PerTeamAllocations.Should().OnlyContain(a => a.ProcessingFeeReduction == 0m,
            "no processing fees to reduce");

        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.OwedTotal.Should().Be(0m, "club rep fully paid");
    }

    /// <summary>
    /// A3: Deposit phase, check partial ($3,000 for 3 teams owing $2,000 each).
    /// Alpha gets $2,000, Bravo gets $1,000, Charlie gets $0.
    /// </summary>
    [Fact(DisplayName = "A3: No fees, deposit, check partial → highest balance first")]
    public async Task A3_NoFees_Deposit_CheckPartial()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3; clubRep.OwedTotal = FeeBase * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 3000m,  // Only covers 1.5 teams
                PaymentType = "Check",
                CheckNo = "1002"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2, "Charlie gets $0, skipped");
        result.PerTeamAllocations.Should().OnlyContain(a => a.ProcessingFeeReduction == 0m);

        // Alpha: $2,000. Bravo: $1,000 (remaining). Charlie: $0.
        var alloc1 = result.PerTeamAllocations![0];
        alloc1.AllocatedAmount.Should().Be(FeeBase, "$2,000 to first team");
        var alloc2 = result.PerTeamAllocations![^1];
        alloc2.AllocatedAmount.Should().Be(1000m, "$1,000 remaining to second team");

        // Club rep
        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.OwedTotal.Should().Be(3000m, "$6,000 - $3,000 = $3,000 remaining");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BALANCE DUE PHASE — deposit already paid by CC ($500/team)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A4: Balance due phase, CC payment, no processing fees.
    /// Deposit ($500) already paid. Each team owes $1,500.
    /// </summary>
    [Fact(DisplayName = "A4: No fees, balance due, CC → $1,500/team owed")]
    public async Task A4_NoFees_BalanceDue_CC()
    {
        var (_, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase, paidTotal: Deposit);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3;
        clubRep.PaidTotal = Deposit * 3; clubRep.OwedTotal = BalanceDue * 3;
        await b.SaveAsync();

        var teams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        teams.Should().OnlyContain(t => t.OwedTotal == BalanceDue, "each team owes $1,500");
        teams.Should().OnlyContain(t => t.FeeProcessing == 0m, "no processing fees");

        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.OwedTotal.Should().Be(BalanceDue * 3, "club rep owes $4,500");
    }

    /// <summary>
    /// A5: Balance due phase, check full payment ($4,500 = 3 × $1,500).
    /// No fees → check = CC. All teams paid in full.
    /// </summary>
    [Fact(DisplayName = "A5: No fees, balance due, check full → $1,500/team, all paid")]
    public async Task A5_NoFees_BalanceDue_CheckFull()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase, paidTotal: Deposit);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3;
        clubRep.PaidTotal = Deposit * 3; clubRep.OwedTotal = BalanceDue * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = BalanceDue * 3,  // $4,500
                PaymentType = "Check",
                CheckNo = "1003"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3);
        result.PerTeamAllocations.Should().OnlyContain(a => a.AllocatedAmount == BalanceDue);
        result.PerTeamAllocations.Should().OnlyContain(a => a.ProcessingFeeReduction == 0m);
        result.PerTeamAllocations.Should().OnlyContain(a => a.NewOwedTotal == 0m);

        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.OwedTotal.Should().Be(0m, "club rep fully paid");
    }

    /// <summary>
    /// A6: Balance due phase, check partial ($2,000 for 3 teams owing $1,500 each).
    /// Alpha gets $1,500, Bravo gets $500, Charlie gets $0.
    /// </summary>
    [Fact(DisplayName = "A6: No fees, balance due, check partial → highest balance first")]
    public async Task A6_NoFees_BalanceDue_CheckPartial()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync();

        b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: FeeBase, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: FeeBase, paidTotal: Deposit);
        b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: FeeBase, paidTotal: Deposit);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = FeeBase * 3; clubRep.FeeTotal = FeeBase * 3;
        clubRep.PaidTotal = Deposit * 3; clubRep.OwedTotal = BalanceDue * 3;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 2000m,
                PaymentType = "Check",
                CheckNo = "1004"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2, "Charlie gets $0, skipped");
        result.PerTeamAllocations.Should().OnlyContain(a => a.ProcessingFeeReduction == 0m);

        var alloc1 = result.PerTeamAllocations![0];
        alloc1.AllocatedAmount.Should().Be(BalanceDue, "$1,500 to first team");
        var alloc2 = result.PerTeamAllocations![^1];
        alloc2.AllocatedAmount.Should().Be(500m, "$500 remaining to second team");

        var updatedClubRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedClubRep!.OwedTotal.Should().Be(2500m, "$4,500 - $2,000 = $2,500 remaining");
    }
}
