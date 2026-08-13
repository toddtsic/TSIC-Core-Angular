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
                // These scenarios hand-stamp proc on deposit-shaped fees (rosterFee only) —
                // data only consistent with proc-on-deposit. TRUE keeps the policy-B slice
                // gate inert so the suite tests allocation mechanics, not slicing.
                BApplyProcessingFeesToTeamDeposit = true,
                PaymentMethodsAllowedCode = 7
            });

        // The negative-correction proc RESTORE reads the CC rate through PaymentState
        // hydration (jobRepo), not IFeeResolutionService — mirror the reducer's percent.
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingFeePercent);

        var paymentState = new PaymentStateService(accountingRepo, jobRepo.Object, new FeeRepository(ctx), new TeamRepository(ctx));
        var svc = new TeamSearchService(
            teamRepo, accountingRepo, registrationRepo, jobRepo.Object,
            feeService.Object, paymentState, adnApi.Object, ladtService.Object,
            new Mock<IEmailService>().Object, new Mock<IPaymentService>().Object,
            new Mock<TSIC.API.Services.Teams.IRegisteredTeamShaper>().Object,
            new Mock<TSIC.API.Services.Teams.ITeamRenameService>().Object,
            new Mock<IClubTeamRepository>().Object, new Mock<IScheduleRepository>().Object, logger.Object);

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

    // ═══════════════════════════════════════════════════════════════════
    //  NEGATIVE CORRECTIONS (claw-backs) — signed-corrections ruling 2026-08-13
    //  Allowed ONLY at single-team scope: "which team gets charged back" is the
    //  admin's call, never an allocator's. Club-scope negative = hard reject
    //  (replacing the old silent no-op). No floor: owed may exceed FeeTotal.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Director tries a −$100 correction at CLUB scope.
    /// EXPECTED: Rejected — a negative must name its team. No row written
    ///           (the old code validated, allocated to zero teams, and returned
    ///           Success with nothing recorded — that silent no-op is dead).
    /// </summary>
    [Fact(DisplayName = "Negative correction: club scope rejected — must target a specific team")]
    public async Task Correction_Negative_ClubScope_Rejected_NoRecord()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            bAddProcessingFees: false, rosterFee: 500m);

        b.AddTeam(jobId, agId, clubRepRegistrationId: clubRepId,
            teamName: "Eagles 2027", feeBase: 500m, feeProcessing: 0m);
        await b.SaveAsync();

        var result = await svc.RecordCheckForClubAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            {
                ClubRepRegistrationId = clubRepId,
                Amount = -100m,
                PaymentType = "Correction"
            });

        result.Success.Should().BeFalse("club-scope distribution has no sensible ordering for a claw-back");
        result.Error.Should().Contain("specific team");

        var recordCount = await ctx.RegistrationAccounting
            .CountAsync(r => r.RegistrationId == clubRepId);
        recordCount.Should().Be(0, "rejection must write nothing — no silent no-op, no partial row");
    }

    /// <summary>
    /// SCENARIO: Team owes $500 (no proc), nothing paid. Director records a −$200
    ///           correction in TEAM scope (reinstate a charge).
    /// RECORD CREATED: Correction, Payamt=−$200, TeamId set, RegistrationId = club rep
    /// TEAM AFTER: PaidTotal=−$200, OwedTotal=$700 (no floor)
    /// CLUB REP AFTER: re-aggregated from teams — PaidTotal=−$200, OwedTotal=$700
    /// </summary>
    [Fact(DisplayName = "Negative correction: −$200 team scope → one signed row, team owes $700")]
    public async Task Correction_Negative_SingleTeam_RaisesOwed()
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
                Amount = -200m,
                PaymentType = "Correction",
                Comment = "Reinstate dropped charge"
            });

        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(1, "exactly the admin-named team — no distribution");
        result.PerTeamAllocations![0].AllocatedAmount.Should().Be(-200m);

        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.TeamId == team.TeamId);
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.CorrectionMethodId);
        record.Payamt.Should().Be(-200m, "appended signed row — nothing edited or voided");
        record.RegistrationId.Should().Be(clubRepId, "team rows always belong to the club rep's account");

        var updatedTeam = await ctx.Teams.FindAsync(team.TeamId);
        updatedTeam!.PaidTotal.Should().Be(-200m);
        updatedTeam.OwedTotal.Should().Be(700m, "$500 fee − (−$200 paid) = $700 — no floor");

        var updatedRep = await ctx.Registrations.FindAsync(clubRepId);
        updatedRep!.PaidTotal.Should().Be(-200m, "rep re-aggregates from its teams");
        updatedRep.OwedTotal.Should().Be(700m);
    }

    /// <summary>
    /// SCENARIO: Proc job (3.5%). Team $500 + $17.50 proc. +$200 correction
    ///           (proc → $10.50), then −$200 correction.
    /// EXPECTED: Exact round-trip — proc back to $17.50, OwedTotal back to $517.50.
    ///           Restore formula $200 × 3.5% == headroom to the post-row target.
    /// </summary>
    [Fact(DisplayName = "Negative correction: team +$200/−$200 round-trips FeeProcessing exactly")]
    public async Task Correction_Negative_SingleTeam_RoundTrip_RestoresProc()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, bAddProcessingFees: true, rosterFee: 500m);

        var team = b.AddTeam(jobId, agId, clubRepRegistrationId: clubRepId,
            teamName: "Eagles 2027", feeBase: 500m, feeProcessing: 17.50m);

        var clubRep = await ctx.Registrations.FindAsync(clubRepId);
        clubRep!.FeeBase = 500m; clubRep.FeeTotal = 517.50m; clubRep.OwedTotal = 517.50m;
        await b.SaveAsync();

        var forgive = await svc.RecordCheckForTeamAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            { TeamId = team.TeamId, ClubRepRegistrationId = clubRepId, Amount = 200m, PaymentType = "Correction" });
        forgive.Success.Should().BeTrue();

        var midpoint = await ctx.Teams.FindAsync(team.TeamId);
        midpoint!.FeeProcessing.Should().Be(10.50m, "reduced by $200 × 3.5% = $7.00 on the way down");

        var clawBack = await svc.RecordCheckForTeamAsync(jobId, UserId,
            new TeamCheckOrCorrectionRequest
            { TeamId = team.TeamId, ClubRepRegistrationId = clubRepId, Amount = -200m, PaymentType = "Correction" });
        clawBack.Success.Should().BeTrue();
        clawBack.PerTeamAllocations![0].ProcessingFeeReduction.Should().Be(-7.00m,
            "a restore is reported as a negative reduction");

        var updatedTeam = await ctx.Teams.FindAsync(team.TeamId);
        updatedTeam!.FeeProcessing.Should().Be(17.50m, "restored by $200 × 3.5% on the way back");
        updatedTeam.PaidTotal.Should().Be(0m, "+$200 and −$200 rows net to zero");
        updatedTeam.OwedTotal.Should().Be(517.50m, "round-trip leaves the books exactly where they started");
    }
}
