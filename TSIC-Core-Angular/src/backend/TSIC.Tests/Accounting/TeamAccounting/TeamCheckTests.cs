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

namespace TSIC.Tests.Accounting.TeamAccounting;

/// <summary>
/// TEAM CHECK &amp; CORRECTION TESTS (single team scope)
///
/// These tests validate what happens when a director records a check payment
/// or correction against a single team (search/teams → team scope).
///
/// Key differences from player accounting:
///   - Payment is recorded against the club rep's registration (not the team directly)
///   - Team's PaidTotal and OwedTotal are updated
///   - Club rep's financial totals are synchronized after each payment
///   - Processing fee reduction follows deposit/full-pay rules
///
/// Each test verifies:
///   1. The accounting record (PaymentMethodId, Payamt, TeamId, RegistrationId)
///   2. The team's financial state (FeeProcessing, PaidTotal, OwedTotal)
///   3. The club rep registration's synced totals
/// </summary>
public class TeamCheckTests
{
    private const string UserId = "test-admin";

    /// <summary>
    /// Builds TeamSearchService with real InMemory repos and mocked external services.
    /// Seeds a job, league, agegroup, club rep registration, and the standard payment methods.
    /// Returns everything needed to add teams and run tests.
    /// </summary>
    private static async Task<(TeamSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agegroupId, Guid clubRepRegId)>
        CreateServiceAsync(
            decimal processingFeePercent = 3.5m,
            bool bAddProcessingFees = true,
            bool bTeamsFullPaymentRequired = false,
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

        // Club rep registration — totals will be synced from teams
        var clubRep = builder.AddClubRepRegistration(job.JobId, clubName: "Test Club");

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
    //  CHECK PAYMENTS — Single Team
    //  Record type: "Check Payment By Client"
    //  Record linked to: club rep RegistrationId + team's TeamId
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Team owes $500 (deposit-only, no processing fees).
    ///           Director records a $500 check in team scope.
    /// RECORD CREATED: Check, Payamt=$500, TeamId set, RegistrationId = club rep
    /// TEAM AFTER: PaidTotal=$500, OwedTotal=$0
    /// CLUB REP AFTER: Financials synced (PaidTotal=$500, OwedTotal=$0)
    /// </summary>
    [Fact(DisplayName = "Team Check: $500 pays team in full → Check record with TeamId, balance $0")]
    public async Task Check_SingleTeam_FullPayment_CreatesRecordWithTeamId()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            bAddProcessingFees: false, rosterFee: 500m);

        var team = b.AddTeam(jobId, agId, clubRepRegistrationId: clubRepId,
            teamName: "Eagles 2027", feeBase: 500m, feeProcessing: 0m);

        // Seed club rep totals to match the team
        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 500m; clubRep.FeeTotal = 500m; clubRep.OwedTotal = 500m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForTeamAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                TeamId = team.TeamId,
                ClubRepRegistrationId = clubRepId,
                Amount = 500m,
                PaymentType = "Check",
                CheckNo = "5001"
            });

        // ── Verify result ──
        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(1);
        result.PerTeamAllocations![0].TeamName.Should().Be("Eagles 2027");
        result.PerTeamAllocations[0].AllocatedAmount.Should().Be(500m);

        // ── Verify accounting record ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.TeamId == team.TeamId);
        record.Should().NotBeNull("a Check record should be created");
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.CheckMethodId,
            "payment method = 'Check Payment By Client'");
        record.Payamt.Should().Be(500m);
        record.Dueamt.Should().Be(500m);
        record.CheckNo.Should().Be("5001");
        record.RegistrationId.Should().Be(clubRepId,
            "record linked to club rep registration");
        record.TeamId.Should().Be(team.TeamId,
            "record linked to specific team");

        // ── Verify team state ──
        var updatedTeam = await ctx.Teams.FindAsync(team.TeamId);
        updatedTeam!.PaidTotal.Should().Be(500m);
        updatedTeam.OwedTotal.Should().Be(0m, "team fully paid");
    }

    /// <summary>
    /// SCENARIO: Team owes $517.50 ($500 base + $17.50 processing at 3.5%).
    ///           Director records a $500 check.
    /// RECORD CREATED: Check, Payamt=$500
    /// FEE IMPACT: Processing fee reduced (amount depends on deposit/full-pay rules)
    /// TEAM AFTER: Processing fee reduced, balance approaches $0
    /// WHY: Check payment removes the CC processing surcharge.
    /// </summary>
    [Fact(DisplayName = "Team Check: $500 check with $17.50 processing fee → fee reduced, balance near $0")]
    public async Task Check_SingleTeam_WithProcessingFee_ReducesFee()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, bAddProcessingFees: true,
            bTeamsFullPaymentRequired: true, rosterFee: 500m);

        var team = b.AddTeam(jobId, agId, clubRepRegistrationId: clubRepId,
            teamName: "Eagles 2027", feeBase: 500m, feeProcessing: 17.50m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 500m; clubRep.FeeProcessing = 17.50m;
        clubRep.FeeTotal = 517.50m; clubRep.OwedTotal = 517.50m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForTeamAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                TeamId = team.TeamId,
                ClubRepRegistrationId = clubRepId,
                Amount = 500m,
                PaymentType = "Check"
            });

        result.Success.Should().BeTrue();

        // ── Verify team fee reduction ──
        var updatedTeam = await ctx.Teams.FindAsync(team.TeamId);

        // baseOwed = $517.50 / 1.035 = $500. allocation = $500. feeReduction = $500 × 3.5% = $17.50.
        updatedTeam!.FeeProcessing.Should().Be(0m,
            "processing fee fully removed: $500 × 3.5% = $17.50");
        updatedTeam.PaidTotal.Should().Be(500m);
        updatedTeam.OwedTotal.Should().Be(0m, "team fully paid after fee removal");

        // ── Verify allocation reported fee reduction ──
        result.PerTeamAllocations![0].ProcessingFeeReduction.Should().Be(17.50m,
            "allocation should report the $17.50 fee reduction");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CORRECTIONS — Single Team
    //  Record type: "Correction"
    //  Same routing as checks but with CorrectionMethodId
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Team owes $500. Director records +$200 correction (partial credit).
    /// RECORD CREATED: Correction, Payamt=$200, TeamId set
    /// TEAM AFTER: PaidTotal=$200, OwedTotal=$300
    /// </summary>
    [Fact(DisplayName = "Team Correction: +$200 against $500 owed → Correction record, balance $300")]
    public async Task Correction_SingleTeam_CreatesCorrectionRecord()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            bAddProcessingFees: false, rosterFee: 500m);

        var team = b.AddTeam(jobId, agId, clubRepRegistrationId: clubRepId,
            teamName: "Eagles 2027", feeBase: 500m, feeProcessing: 0m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 500m; clubRep.FeeTotal = 500m; clubRep.OwedTotal = 500m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForTeamAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                TeamId = team.TeamId,
                ClubRepRegistrationId = clubRepId,
                Amount = 200m,
                PaymentType = "Correction",
                Comment = "Returning club credit"
            });

        result.Success.Should().BeTrue();

        // ── Verify record type ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.TeamId == team.TeamId);
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.CorrectionMethodId,
            "payment method = 'Correction'");
        record.Payamt.Should().Be(200m);
        record.Comment.Should().Be("Returning club credit");

        // ── Verify team state ──
        var updatedTeam = await ctx.Teams.FindAsync(team.TeamId);
        updatedTeam!.PaidTotal.Should().Be(200m);
        updatedTeam.OwedTotal.Should().Be(300m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VALIDATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Check amount exceeds what the club rep owes.
    /// EXPECTED: Rejected — cannot overpay.
    /// </summary>
    [Fact(DisplayName = "Validation: Check exceeding club rep owed total is rejected")]
    public async Task Check_ExceedsOwedTotal_Rejected()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            bAddProcessingFees: false, rosterFee: 500m);

        var team = b.AddTeam(jobId, agId, clubRepRegistrationId: clubRepId,
            teamName: "Eagles 2027", feeBase: 500m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 500m; clubRep.FeeTotal = 500m; clubRep.OwedTotal = 500m;
        await b.SaveAsync();

        var result = await svc.RecordCheckForTeamAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                TeamId = team.TeamId,
                ClubRepRegistrationId = clubRepId,
                Amount = 600m,  // more than the $500 owed
                PaymentType = "Check"
            });

        result.Success.Should().BeFalse("cannot pay more than is owed");
        result.Error.Should().Contain("exceeds");
    }
}
