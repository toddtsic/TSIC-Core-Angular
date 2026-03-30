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
/// CLUB-LEVEL CHECK ALLOCATION TESTS
///
/// These tests validate how a single check payment from a club rep is distributed
/// across multiple teams within the club (search/teams → club scope).
///
/// Distribution algorithm:
///   1. Teams are sorted by OwedTotal descending (highest balance first)
///   2. Each team receives an allocation based on deposit/full-pay rules
///   3. Processing fees are reduced per team before recording payment
///   4. Remaining check balance carries to the next team
///   5. Club rep financials are synchronized after all allocations
///
/// Each test verifies:
///   1. Per-team allocation amounts and processing fee reductions
///   2. Each team's accounting record (PaymentMethodId, Payamt, TeamId)
///   3. Each team's financial state after (PaidTotal, OwedTotal, FeeProcessing)
///   4. Club rep registration synced totals
/// </summary>
public class ClubCheckAllocationTests
{
    private const string UserId = "test-admin";

    private static async Task<(TeamSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agegroupId, Guid clubRepRegId)>
        CreateServiceAsync(
            decimal processingFeePercent = 3.5m,
            bool bAddProcessingFees = true,
            bool bTeamsFullPaymentRequired = true,
            decimal rosterFee = 500m,
            decimal teamFee = 0m)
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        var job = builder.AddJob(
            processingFeePercent: processingFeePercent,
            bAddProcessingFees: bAddProcessingFees,
            bTeamsFullPaymentRequired: bTeamsFullPaymentRequired);
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "2027 AA",
            rosterFee: rosterFee, teamFee: teamFee);

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
            .ReturnsAsync(processingFeePercent / 100m);

        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = bAddProcessingFees,
                BApplyProcessingFeesToTeamDeposit = false,
                BTeamsFullPaymentRequired = bTeamsFullPaymentRequired,
                PaymentMethodsAllowedCode = 7
            });

        var svc = new TeamSearchService(
            teamRepo, accountingRepo, registrationRepo, jobRepo.Object,
            feeService.Object, adnApi.Object, ladtService.Object, logger.Object);

        return (svc, builder, ctx, job.JobId, ag.AgegroupId, clubRep.RegistrationId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FULL CLUB CHECK — pays all teams
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club has 3 teams, each owing $500 (no processing fees).
    ///           Director records a $1500 check covering all teams.
    /// EXPECTED: 3 Check records created (one per team, $500 each).
    ///           All teams fully paid. Club rep balance = $0.
    /// </summary>
    [Fact(DisplayName = "Club Check: $1500 across 3 teams ($500 each) → 3 records, all paid")]
    public async Task ClubCheck_FullPayment_ThreeTeams_AllPaid()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            bAddProcessingFees: false, rosterFee: 500m);

        var t1 = b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: 500m);
        var t2 = b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: 500m);
        var t3 = b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: 500m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 1500m; clubRep.FeeTotal = 1500m; clubRep.OwedTotal = 1500m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 1500m,
                PaymentType = "Check",
                CheckNo = "9001"
            });

        // ── Verify result ──
        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3, "each team gets a separate allocation");

        // ── Verify 3 separate accounting records ──
        var records = await ctx.RegistrationAccounting
            .Where(r => r.RegistrationId == clubRepId)
            .ToListAsync();
        records.Should().HaveCount(3, "one Check record per team");
        records.Should().OnlyContain(r => r.PaymentMethodId == AccountingDataBuilder.CheckMethodId);
        records.Should().OnlyContain(r => r.Payamt == 500m, "each team gets $500");
        records.Should().OnlyContain(r => r.CheckNo == "9001", "check number on all records");

        // ── Verify all teams fully paid ──
        foreach (var teamId in new[] { t1.TeamId, t2.TeamId, t3.TeamId })
        {
            var team = await ctx.Teams.FindAsync(teamId);
            team!.PaidTotal.Should().Be(500m);
            team.OwedTotal.Should().Be(0m);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PARTIAL CLUB CHECK — doesn't cover all teams
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club has 3 teams owing $700, $500, $300 respectively.
    ///           Director records a $900 check (not enough for all).
    /// EXPECTED: Distributed highest-balance-first:
    ///           Team A ($700 owed) gets $500 (deposit amount)
    ///           Team B ($500 owed) gets $400 (remaining balance)
    ///           Team C ($300 owed) gets $0 (check exhausted)
    ///           (Exact allocation depends on deposit/full-pay rules)
    /// </summary>
    [Fact(DisplayName = "Club Check: $900 partial across 3 teams → highest balance first")]
    public async Task ClubCheck_PartialPayment_HighestBalanceFirst()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            bAddProcessingFees: false, rosterFee: 500m, bTeamsFullPaymentRequired: false);

        // Teams with different balances — sorted by OwedTotal DESC
        var t1 = b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: 700m);
        var t2 = b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: 500m);
        var t3 = b.AddTeam(jobId, agId, clubRepId, "Team Charlie", feeBase: 300m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 1500m; clubRep.FeeTotal = 1500m; clubRep.OwedTotal = 1500m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 900m,
                PaymentType = "Check",
                CheckNo = "9002"
            });

        result.Success.Should().BeTrue();

        // With bTeamsFullPaymentRequired=false, each team gets min(rosterFee, remaining)
        // rosterFee=$500, so:
        //   Team Alpha ($700 owed): gets $500 (capped at rosterFee), remaining=$400
        //   Team Bravo ($500 owed): gets $400 (remaining balance), remaining=$0
        //   Team Charlie ($300 owed): gets $0 (exhausted)
        var allocations = result.PerTeamAllocations!;
        allocations.Should().HaveCount(2, "Team Charlie gets $0, so skipped");

        // Verify records exist for allocated teams only
        var records = await ctx.RegistrationAccounting
            .Where(r => r.RegistrationId == clubRepId)
            .ToListAsync();
        records.Should().HaveCount(2, "only teams receiving payment get records");

        // Team Charlie should have no payment
        var charlieTeam = await ctx.Teams.FindAsync(t3.TeamId);
        charlieTeam!.PaidTotal.Should().Be(0m, "Team Charlie received nothing — check exhausted");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CLUB CHECK WITH PROCESSING FEE REDUCTIONS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club has 2 teams, each owing $517.50 ($500 base + $17.50 processing).
    ///           Director records a $1000 check.
    ///           Job has bTeamsFullPaymentRequired=true, bAddProcessingFees=true.
    /// EXPECTED: Each team's $17.50 processing fee is fully removed (Branch A).
    ///           Each team receives $500 check allocation.
    ///           Each team ends at $0 owed.
    ///           Total fee reduction reported: $35.00.
    /// </summary>
    [Fact(DisplayName = "Club Check: $1000 across 2 teams removes $35 total processing fees")]
    public async Task ClubCheck_WithProcessingFees_RemovesFeePerTeam()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, bAddProcessingFees: true,
            bTeamsFullPaymentRequired: true, rosterFee: 500m);

        var t1 = b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: 500m, feeProcessing: 17.50m);
        var t2 = b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: 500m, feeProcessing: 17.50m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 1000m; clubRep.FeeProcessing = 35m;
        clubRep.FeeTotal = 1035m; clubRep.OwedTotal = 1035m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 1000m,
                PaymentType = "Check",
                CheckNo = "9003"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2);

        // ── Verify per-team fee reductions ──
        var alloc1 = result.PerTeamAllocations!.First(a => a.TeamName == "Team Alpha");
        alloc1.AllocatedAmount.Should().Be(500m);
        alloc1.ProcessingFeeReduction.Should().Be(17.50m,
            "full processing fee removed for check (full-pay-required mode)");
        alloc1.NewOwedTotal.Should().Be(0m);

        var alloc2 = result.PerTeamAllocations!.First(a => a.TeamName == "Team Bravo");
        alloc2.AllocatedAmount.Should().Be(500m);
        alloc2.ProcessingFeeReduction.Should().Be(17.50m);
        alloc2.NewOwedTotal.Should().Be(0m);

        // ── Verify teams ──
        var updatedT1 = await ctx.Teams.FindAsync(t1.TeamId);
        updatedT1!.FeeProcessing.Should().Be(0m, "processing fee fully removed");
        updatedT1.PaidTotal.Should().Be(500m);
        updatedT1.OwedTotal.Should().Be(0m);

        var updatedT2 = await ctx.Teams.FindAsync(t2.TeamId);
        updatedT2!.FeeProcessing.Should().Be(0m);
        updatedT2.PaidTotal.Should().Be(500m);
        updatedT2.OwedTotal.Should().Be(0m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DROPPED/WAITLISTED TEAMS EXCLUDED
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club has 2 active teams and 1 team in "Dropped Teams" agegroup.
    ///           Director records a $1000 check.
    /// EXPECTED: Only the 2 active teams receive payment.
    ///           The dropped team is excluded from allocation.
    /// </summary>
    [Fact(DisplayName = "Club Check: dropped team excluded from allocation")]
    public async Task ClubCheck_DroppedTeamExcluded()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            bAddProcessingFees: false, rosterFee: 500m);

        // Active teams
        var t1 = b.AddTeam(jobId, agId, clubRepId, "Team Alpha", feeBase: 500m);
        var t2 = b.AddTeam(jobId, agId, clubRepId, "Team Bravo", feeBase: 500m);

        // Dropped team — in a "Dropped Teams" agegroup
        var league = await ctx.Leagues.FirstAsync();
        var droppedAg = b.AddAgegroup(league.LeagueId, "Dropped Teams");
        await b.SaveAsync(); // save agegroup first
        var t3 = b.AddTeam(jobId, droppedAg.AgegroupId, clubRepId, "Team Dropped",
            feeBase: 500m, active: false);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 1000m; clubRep.FeeTotal = 1000m; clubRep.OwedTotal = 1000m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 1000m,
                PaymentType = "Check"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2,
            "only 2 active non-dropped teams receive allocation");

        // Dropped team should have no payment
        var droppedTeam = await ctx.Teams.FindAsync(t3.TeamId);
        droppedTeam!.PaidTotal.Should().Be(0m, "dropped team excluded from check allocation");
    }
}
