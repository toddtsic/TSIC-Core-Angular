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
/// TEAM ECHECK TESTS (single team scope)
///
/// These tests validate what happens when a director records an eCheck payment
/// against a single team (search/teams → team scope).
///
/// Mirror of TeamCheckTests, but for the partial-credit eCheck flavor:
///   - PaymentMethodId = EcheckMethodId ("E-Check Payment")
///   - baseOwed calc still uses the CC rate (FeeProcessing was originally sized at CC rate)
///   - Per-team fee reduction = allocation × (CC_rate − EC_rate), NOT full CC rate
///   - Customer still owes the EC rate portion after the eCheck clears
/// </summary>
public class TeamEcheckTests
{
    private const string UserId = "test-admin";

    private static async Task<(TeamSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agegroupId, Guid clubRepRegId)>
        CreateServiceAsync(
            decimal processingFeePercent = 3.5m,
            decimal ecprocessingFeePercent = 1.5m,
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
    //  ECHECK PAYMENTS — Single Team
    //  Record type: "E-Check Payment"
    //  Record linked to: club rep RegistrationId + team's TeamId
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Team owes $500 (no processing fees). Director records a $500 eCheck.
    /// RECORD CREATED: ECheck, Payamt=$500, TeamId set, RegistrationId = club rep
    /// TEAM AFTER: PaidTotal=$500, OwedTotal=$0
    /// </summary>
    [Fact(DisplayName = "Team ECheck: $500 pays team in full → ECheck record with TeamId, balance $0")]
    public async Task Echeck_SingleTeam_FullPayment_CreatesRecordWithTeamId()
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
                Amount = 500m,
                PaymentType = "ECheck",
                CheckNo = "EC-5001"
            });

        // ── Verify result ──
        result.Success.Should().BeTrue();
        result.PerTeamAllocations.Should().HaveCount(1);
        result.PerTeamAllocations![0].TeamName.Should().Be("Eagles 2027");
        result.PerTeamAllocations[0].AllocatedAmount.Should().Be(500m);

        // ── Verify accounting record ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.TeamId == team.TeamId);
        record.Should().NotBeNull("an ECheck record should be created");
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.EcheckMethodId,
            "payment method = 'E-Check Payment'");
        record.Payamt.Should().Be(500m);
        record.Dueamt.Should().Be(500m);
        record.CheckNo.Should().Be("EC-5001");
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
    /// SCENARIO: Team owes $517.50 ($500 base + $17.50 CC processing at 3.5%).
    ///           Director records a $500 eCheck (CC=3.5%, EC=1.5%, diff=2.0%).
    /// RECORD CREATED: ECheck, Payamt=$500
    /// FEE IMPACT:
    ///   - baseOwed = $517.50 / 1.035 = $500 (CC rate, since FeeProcessing was sized at CC)
    ///   - allocation = $500
    ///   - processingFeeReduction = $500 × 2.0% = $10.00 (NOT $17.50 like Check)
    ///   - team.FeeProcessing: $17.50 → $7.50 (eCheck rate portion remains)
    ///   - team.OwedTotal: $7.50 (still owes the EC rate)
    /// </summary>
    [Fact(DisplayName = "Team ECheck: $500 reduces fee by $10.00 (CC−EC diff), $7.50 EC fee remains")]
    public async Task Echeck_SingleTeam_WithProcessingFee_ReducesFeeByDiff()
    {
        var (svc, b, ctx, jobId, agId, clubRepId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, ecprocessingFeePercent: 1.5m,
            bAddProcessingFees: true, bTeamsFullPaymentRequired: true, rosterFee: 500m);

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
                PaymentType = "ECheck"
            });

        result.Success.Should().BeTrue();

        // ── Verify partial fee reduction (NOT full CC like mail-in) ──
        var updatedTeam = await ctx.Teams.FindAsync(team.TeamId);
        updatedTeam!.FeeProcessing.Should().Be(7.50m,
            "$500 × (3.5% − 1.5%) = $10.00 reduction; was $17.50 → $7.50");
        updatedTeam.PaidTotal.Should().Be(500m);
        updatedTeam.OwedTotal.Should().Be(7.50m,
            "still owes the eCheck rate portion ($500 × 1.5% = $7.50)");

        // ── Verify allocation reported partial reduction ──
        result.PerTeamAllocations![0].ProcessingFeeReduction.Should().Be(10.00m,
            "partial credit: $500 × (CC 3.5% − EC 1.5%) = $10.00");
        result.PerTeamAllocations[0].NewOwedTotal.Should().Be(7.50m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VALIDATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: ECheck amount exceeds what the club rep owes.
    /// EXPECTED: Rejected — cannot overpay (same guard as Check).
    /// </summary>
    [Fact(DisplayName = "Validation: ECheck exceeding club rep owed total is rejected")]
    public async Task Echeck_ExceedsOwedTotal_Rejected()
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
                PaymentType = "ECheck"
            });

        result.Success.Should().BeFalse("cannot pay more than is owed");
        result.Error.Should().Contain("exceeds");
    }
}
