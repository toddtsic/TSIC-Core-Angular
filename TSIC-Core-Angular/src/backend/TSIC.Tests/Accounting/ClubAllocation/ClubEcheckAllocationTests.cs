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
/// CLUB-LEVEL ECHECK ALLOCATION TESTS
///
/// Mirror of ClubCheckAllocationTests, but for eCheck (partial fee credit).
///
/// Same allocation algorithm as Check:
///   1. Teams sorted by OwedTotal DESC (highest first)
///   2. baseOwed = team.OwedTotal / (1 + CC_rate)  ← still CC rate, FeeProcessing was sized at CC
///   3. allocation = min(baseOwed, remaining)
///
/// Key eCheck difference:
///   processingFeeReduction = allocation × (CC_rate − EC_rate)   ← NOT full CC rate
///
/// Result: customer still owes the EC rate portion on each team after eCheck clears.
/// </summary>
public class ClubEcheckAllocationTests
{
    private const string UserId = "test-admin";

    private static async Task<(TeamSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agegroupId, Guid clubRepRegId)>
        CreateServiceAsync(
            decimal processingFeePercent = 3.5m,
            decimal ecprocessingFeePercent = 1.5m,
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
        feeService.Setup(f => f.GetEffectiveEcheckProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ecprocessingFeePercent / 100m);

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
    //  CLUB ECHECK WITH PROCESSING FEE REDUCTIONS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club has 2 teams, each owing $517.50 ($500 base + $17.50 CC processing).
    ///           Director records a $1000 eCheck (CC=3.5%, EC=1.5%, diff=2.0%).
    /// EXPECTED: Each team's baseOwed = $517.50 / 1.035 = $500 (CC rate).
    ///           Allocation = $500 per team. FeeReduction = $500 × 2.0% = $10.00.
    ///           Each team retains $7.50 EC processing fee. Total $15.00 still owed.
    /// </summary>
    [Fact(DisplayName = "Club ECheck: $1000 across 2 teams removes $20 (CC−EC), $15 EC fee remains")]
    public async Task ClubEcheck_TwoTeams_ReducesFeeByDiff()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, ecprocessingFeePercent: 1.5m,
            bAddProcessingFees: true, bTeamsFullPaymentRequired: true, rosterFee: 500m);

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
                PaymentType = "ECheck",
                CheckNo = "EC-9003"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(2);

        // ── Each allocation: $500 base + $10.00 reduction (NOT $17.50) ──
        var alloc1 = result.PerTeamAllocations!.First(a => a.TeamName == "Team Alpha");
        alloc1.AllocatedAmount.Should().Be(500m,
            "baseOwed = $517.50 / 1.035 = $500 (CC rate)");
        alloc1.ProcessingFeeReduction.Should().Be(10.00m,
            "$500 × (3.5% − 1.5%) = $10.00 (NOT full $17.50)");
        alloc1.NewOwedTotal.Should().Be(7.50m,
            "$7.50 EC processing fee remains");

        var alloc2 = result.PerTeamAllocations!.First(a => a.TeamName == "Team Bravo");
        alloc2.AllocatedAmount.Should().Be(500m);
        alloc2.ProcessingFeeReduction.Should().Be(10.00m);
        alloc2.NewOwedTotal.Should().Be(7.50m);

        // ── Verify each team retains the EC rate portion ──
        var updatedT1 = await ctx.Teams.FindAsync(t1.TeamId);
        updatedT1!.FeeProcessing.Should().Be(7.50m, "EC processing fee remains");
        updatedT1.PaidTotal.Should().Be(500m);
        updatedT1.OwedTotal.Should().Be(7.50m);

        var updatedT2 = await ctx.Teams.FindAsync(t2.TeamId);
        updatedT2!.FeeProcessing.Should().Be(7.50m);
        updatedT2.PaidTotal.Should().Be(500m);
        updatedT2.OwedTotal.Should().Be(7.50m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PARTIAL CLUB ECHECK WITH PROCESSING FEES
    //  Mirror of the Check "nightmare" scenario.
    //  Verifies baseOwed uses CC rate while reduction uses (CC − EC) diff.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Club has 3 teams with different OwedTotals:
    ///           Team Alpha:   owes $621.00 ($600 base + $21.00 CC processing) — highest
    ///           Team Bravo:   owes $517.50 ($500 base + $17.50 CC processing)
    ///           Team Charlie: owes $414.00 ($400 base + $14.00 CC processing) — lowest
    ///           Director records a $1300 eCheck (CC=3.5%, EC=1.5%, diff=2.0%).
    /// EXPECTED:
    ///   Alpha:   baseOwed=$621/1.035=$600. allocation=$600. fee reduction=$600×2%=$12.
    ///            FeeProcessing: $21→$9. OwedTotal=$9. (EC rate $9 remains.)
    ///   Bravo:   baseOwed=$517.50/1.035=$500. allocation=$500. fee reduction=$500×2%=$10.
    ///            FeeProcessing: $17.50→$7.50. OwedTotal=$7.50.
    ///   Charlie: baseOwed=$414/1.035=$400. remaining=$1300−$600−$500=$200.
    ///            allocation=min($400,$200)=$200. fee reduction=$200×2%=$4.
    ///            FeeProcessing: $14→$10. OwedTotal=$210 ($400 base + $10 fee − $200 paid).
    /// </summary>
    [Fact(DisplayName = "Club ECheck: $1300 partial → reduction by (CC−EC), EC rate retained per team")]
    public async Task ClubEcheck_PartialWithProcessingFees_ProportionalReductionByDiff()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, ecprocessingFeePercent: 1.5m,
            bAddProcessingFees: true, bTeamsFullPaymentRequired: true, rosterFee: 500m);

        var t1 = b.AddTeam(jobId, agId, clubRepId, "Team Alpha",
            feeBase: 600m, feeProcessing: 21.00m);
        var t2 = b.AddTeam(jobId, agId, clubRepId, "Team Bravo",
            feeBase: 500m, feeProcessing: 17.50m);
        var t3 = b.AddTeam(jobId, agId, clubRepId, "Team Charlie",
            feeBase: 400m, feeProcessing: 14.00m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 1500m; clubRep.FeeProcessing = 52.50m;
        clubRep.FeeTotal = 1552.50m; clubRep.OwedTotal = 1552.50m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = 1300m,
                PaymentType = "ECheck",
                CheckNo = "EC-9010"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(3, "all 3 teams receive some allocation");

        // ── Team Alpha (highest owed $621): baseOwed=$600, full allocation ──
        var alloc1 = result.PerTeamAllocations!.First(a => a.TeamName == "Team Alpha");
        alloc1.AllocatedAmount.Should().Be(600m,
            "baseOwed = $621 / 1.035 = $600 (CC rate)");
        alloc1.ProcessingFeeReduction.Should().Be(12.00m,
            "$600 × (3.5% − 1.5%) = $12.00");

        var updatedT1 = await ctx.Teams.FindAsync(t1.TeamId);
        updatedT1!.FeeProcessing.Should().Be(9.00m,
            "$21.00 − $12.00 = $9.00 EC rate portion remains");
        updatedT1.PaidTotal.Should().Be(600m);
        updatedT1.OwedTotal.Should().Be(9.00m);

        // ── Team Bravo ($517.50 owed): baseOwed=$500, full allocation ──
        var alloc2 = result.PerTeamAllocations!.First(a => a.TeamName == "Team Bravo");
        alloc2.AllocatedAmount.Should().Be(500m,
            "baseOwed = $517.50 / 1.035 = $500");
        alloc2.ProcessingFeeReduction.Should().Be(10.00m,
            "$500 × (3.5% − 1.5%) = $10.00");

        var updatedT2 = await ctx.Teams.FindAsync(t2.TeamId);
        updatedT2!.FeeProcessing.Should().Be(7.50m,
            "$17.50 − $10.00 = $7.50 EC rate portion remains");
        updatedT2.PaidTotal.Should().Be(500m);
        updatedT2.OwedTotal.Should().Be(7.50m);

        // ── Team Charlie ($414 owed): baseOwed=$400, but remaining=$200 ──
        var alloc3 = result.PerTeamAllocations!.First(a => a.TeamName == "Team Charlie");
        alloc3.AllocatedAmount.Should().Be(200m,
            "remaining check balance ($1300 − $600 − $500 = $200)");
        alloc3.ProcessingFeeReduction.Should().Be(4.00m,
            "$200 × (3.5% − 1.5%) = $4.00");

        var updatedT3 = await ctx.Teams.FindAsync(t3.TeamId);
        updatedT3!.FeeProcessing.Should().Be(10.00m,
            "original $14.00 − $4.00 = $10.00 remaining");
        updatedT3.PaidTotal.Should().Be(200m);
        updatedT3.OwedTotal.Should().Be(210.00m,
            "$400 base + $10.00 processing − $200 paid = $210.00");
    }
}
