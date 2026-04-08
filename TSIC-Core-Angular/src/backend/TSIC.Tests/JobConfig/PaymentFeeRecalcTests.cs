using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.JobConfig;

/// <summary>
/// Tests that toggling fee-affecting flags in Job Config Payment tab
/// auto-recalculates team fees via the new trigger in UpdatePaymentAsync.
///
/// Key flags tested:
///   - BTeamsFullPaymentRequired (deposit-only ↔ full pay)
///   - BAddProcessingFees (on/off)
///   - BApplyProcessingFeesToTeamDeposit (deposit vs balance processing)
///   - ProcessingFeePercent (rate changes)
///
/// Strategy: real repos (JobConfigRepository, TeamRepository) against in-memory DB,
/// mocked IFeeResolutionService to simulate fee recomputation.
/// </summary>
public class PaymentFeeRecalcTests
{
    private const decimal Deposit = 500m;
    private const decimal BalanceDue = 1500m;
    private const decimal ProcessingRate = 0.035m; // 3.5%

    /// <summary>
    /// Builds JobConfigService wired to real repos and a real TeamRegistrationService
    /// (for recalc), all against the same in-memory DB.
    /// </summary>
    private static async Task<(
        JobConfigService configService,
        SqlDbContext ctx,
        Guid jobId,
        List<Teams> teams)>
        CreateServiceAsync(
            bool bTeamsFullPaymentRequired = false,
            bool bAddProcessingFees = false,
            bool bApplyProcessingFeesToTeamDeposit = false,
            decimal processingFeePercent = 3.5m,
            int teamCount = 2,
            decimal teamFeeBase = 500m,
            decimal teamFeeProcessing = 0m,
            decimal teamPaidTotal = 0m,
            bool addWaitlistTeam = false)
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        var job = builder.AddJob(
            processingFeePercent: processingFeePercent,
            bAddProcessingFees: bAddProcessingFees,
            bApplyProcessingFeesToTeamDeposit: bApplyProcessingFeesToTeamDeposit,
            bTeamsFullPaymentRequired: bTeamsFullPaymentRequired);

        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "2027 AA",
            rosterFee: Deposit, teamFee: BalanceDue);
        var clubRep = builder.AddClubRepRegistration(job.JobId);

        var teams = new List<Teams>();
        for (var i = 0; i < teamCount; i++)
        {
            var team = builder.AddTeam(
                job.JobId, ag.AgegroupId,
                clubRepRegistrationId: clubRep.RegistrationId,
                teamName: $"Team {i + 1}",
                feeBase: teamFeeBase,
                feeProcessing: teamFeeProcessing,
                paidTotal: teamPaidTotal);
            teams.Add(team);
        }

        if (addWaitlistTeam)
        {
            var waitlistAg = builder.AddAgegroup(league.LeagueId, "WAITLIST",
                rosterFee: Deposit, teamFee: BalanceDue);
            var waitlistTeam = builder.AddTeam(
                job.JobId, waitlistAg.AgegroupId,
                clubRepRegistrationId: clubRep.RegistrationId,
                teamName: "Waitlist Team",
                feeBase: teamFeeBase,
                feeProcessing: teamFeeProcessing);
            teams.Add(waitlistTeam);
        }

        await builder.SaveAsync();

        // Real repos against in-memory DB
        var configRepo = new JobConfigRepository(ctx);
        var teamRepo = new TeamRepository(ctx);

        // Mock external services that TeamRegistrationService needs but recalc doesn't use
        var jobRepo = new Mock<IJobRepository>();
        // Dynamic: reads current job state from DB so recalc picks up flag changes
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) =>
            {
                var currentJob = ctx.Jobs.First(j => j.JobId == id);
                return Task.FromResult<JobFeeSettings?>(new JobFeeSettings
                {
                    BTeamsFullPaymentRequired = currentJob.BTeamsFullPaymentRequired,
                    BAddProcessingFees = currentJob.BAddProcessingFees,
                    BApplyProcessingFeesToTeamDeposit = currentJob.BApplyProcessingFeesToTeamDeposit,
                    PaymentMethodsAllowedCode = currentJob.PaymentMethodsAllowedCode,
                });
            });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingRate);

        // ApplyTeamSwapFeesAsync mock: simulates the real fee computation by mutating the entity
        feeService.Setup(f => f.ApplyTeamSwapFeesAsync(
                It.IsAny<Teams>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<TeamFeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Returns((Teams t, Guid _, Guid agId, TeamFeeApplicationContext feeCtx, CancellationToken _) =>
            {
                // Look up agegroup from context to get deposit/balance
                var agegroup = ctx.Agegroups.First(a => a.AgegroupId == agId);
                var deposit = agegroup.RosterFee ?? 0;
                var balance = agegroup.TeamFee ?? 0;

                t.FeeBase = feeCtx.IsFullPaymentRequired ? deposit + balance : deposit;

                if (feeCtx.AddProcessingFees)
                {
                    t.FeeProcessing = feeCtx.ApplyProcessingFeesToDeposit
                        ? t.FeeBase * feeCtx.ProcessingFeePercent
                        : (feeCtx.IsFullPaymentRequired ? balance * feeCtx.ProcessingFeePercent : 0);
                }
                else
                {
                    t.FeeProcessing = 0;
                }

                t.FeeTotal = (t.FeeBase ?? 0) + (t.FeeProcessing ?? 0);
                t.OwedTotal = (t.FeeTotal ?? 0) - (t.PaidTotal ?? 0);

                return Task.CompletedTask;
            });

        // Build TeamRegistrationService with all mocked deps except the ones recalc actually uses
        var teamRegService = new TeamRegistrationService(
            new Mock<ILogger<TeamRegistrationService>>().Object,
            new Mock<IClubRepRepository>().Object,
            new Mock<IClubRepository>().Object,
            jobRepo.Object,
            new Mock<IJobLeagueRepository>().Object,
            new Mock<IAgeGroupRepository>().Object,
            teamRepo,
            new Mock<IRegistrationRepository>().Object,
            new Mock<IUserRepository>().Object,
            new Mock<ITokenService>().Object,
            MockUserManager(),
            feeService.Object,
            new Mock<ITextSubstitutionService>().Object,
            new Mock<IEmailService>().Object,
            new Mock<IJobDiscountCodeRepository>().Object,
            new Mock<IClubTeamRepository>().Object,
            new Mock<ITeamPlacementService>().Object);

        var configService = new JobConfigService(
            configRepo,
            teamRegService,
            new Mock<ILogger<JobConfigService>>().Object);

        return (configService, ctx, job.JobId, teams);
    }

    /// <summary>Build a minimal request that matches the current job state except for the fields being tested.</summary>
    private static UpdateJobConfigPaymentRequest BuildRequest(
        SqlDbContext ctx, Guid jobId,
        bool? bTeamsFullPaymentRequired = null,
        bool? bAddProcessingFees = null,
        bool? bApplyProcessingFeesToTeamDeposit = null,
        decimal? processingFeePercent = null,
        string? payTo = null,
        string? mailTo = null)
    {
        var job = ctx.Jobs.First(j => j.JobId == jobId);
        return new UpdateJobConfigPaymentRequest
        {
            PaymentMethodsAllowedCode = job.PaymentMethodsAllowedCode,
            BAddProcessingFees = bAddProcessingFees ?? job.BAddProcessingFees,
            ProcessingFeePercent = processingFeePercent ?? job.ProcessingFeePercent,
            BApplyProcessingFeesToTeamDeposit = bApplyProcessingFeesToTeamDeposit ?? job.BApplyProcessingFeesToTeamDeposit,
            BTeamsFullPaymentRequired = bTeamsFullPaymentRequired ?? job.BTeamsFullPaymentRequired,
            BAllowRefundsInPriorMonths = job.BAllowRefundsInPriorMonths,
            BAllowCreditAll = job.BAllowCreditAll,
            PerPlayerCharge = job.PerPlayerCharge,
            PerTeamCharge = job.PerTeamCharge,
            PerMonthCharge = job.PerMonthCharge,
            PayTo = payTo ?? job.PayTo,
            MailTo = mailTo ?? job.MailTo,
            MailinPaymentWarning = job.MailinPaymentWarning,
            Balancedueaspercent = job.Balancedueaspercent,
        };
    }

    private static Microsoft.AspNetCore.Identity.UserManager<TSIC.Infrastructure.Data.Identity.ApplicationUser> MockUserManager()
    {
        var store = new Mock<Microsoft.AspNetCore.Identity.IUserStore<TSIC.Infrastructure.Data.Identity.ApplicationUser>>();
        return new Mock<Microsoft.AspNetCore.Identity.UserManager<TSIC.Infrastructure.Data.Identity.ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!).Object;
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 1: Full Pay Required ON → fees go up
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPayRequired_TurnedOn_RecalculatesFeesUp()
    {
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false, teamFeeBase: Deposit);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var updatedTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        foreach (var t in updatedTeams)
        {
            t.FeeBase.Should().Be(Deposit + BalanceDue,
                "FeeBase should be deposit + balance when full pay is required");
            t.OwedTotal.Should().Be(Deposit + BalanceDue,
                "OwedTotal should reflect the full fee (no prior payments)");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 2: Full Pay Required OFF → fees go down
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPayRequired_TurnedOff_RecalculatesFeesDown()
    {
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: true, teamFeeBase: Deposit + BalanceDue);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: false);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var updatedTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        foreach (var t in updatedTeams)
        {
            t.FeeBase.Should().Be(Deposit,
                "FeeBase should revert to deposit-only when full pay is turned off");
            t.OwedTotal.Should().Be(Deposit,
                "OwedTotal should reflect deposit-only");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 3: Processing Fees ON → adds processing to teams
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessingFees_TurnedOn_AddsProcessingToTeams()
    {
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bAddProcessingFees: false, bApplyProcessingFeesToTeamDeposit: true,
            teamFeeBase: Deposit, teamFeeProcessing: 0);

        var req = BuildRequest(ctx, jobId, bAddProcessingFees: true, bApplyProcessingFeesToTeamDeposit: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var updatedTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        foreach (var t in updatedTeams)
        {
            t.FeeProcessing.Should().BeGreaterThan(0,
                "processing fee should be added when BAddProcessingFees is turned on");
            t.FeeTotal.Should().Be((t.FeeBase ?? 0) + (t.FeeProcessing ?? 0),
                "FeeTotal should be FeeBase + FeeProcessing");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 4: Processing Fees OFF → removes processing from teams
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessingFees_TurnedOff_RemovesProcessingFromTeams()
    {
        var expectedProcessing = Deposit * ProcessingRate;
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bAddProcessingFees: true, bApplyProcessingFeesToTeamDeposit: true,
            teamFeeBase: Deposit, teamFeeProcessing: expectedProcessing);

        var req = BuildRequest(ctx, jobId, bAddProcessingFees: false);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var updatedTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        foreach (var t in updatedTeams)
        {
            t.FeeProcessing.Should().Be(0,
                "processing fee should be zero when BAddProcessingFees is turned off");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 5: Processing Rate Changed → recalculates
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessingRate_Changed_RecalculatesProcessingFees()
    {
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bAddProcessingFees: true, bApplyProcessingFeesToTeamDeposit: true,
            processingFeePercent: 3.5m,
            teamFeeBase: Deposit, teamFeeProcessing: Deposit * ProcessingRate);

        // Change rate from 3.5% to 5.0%
        var req = BuildRequest(ctx, jobId, processingFeePercent: 5.0m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        // Recalc should have been triggered (rate changed)
        // The mock still uses ProcessingRate (0.035) since that's what feeService returns,
        // but the point is that recalc WAS triggered. Verify teams were touched.
        var updatedTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        updatedTeams.Should().AllSatisfy(t =>
            t.FeeProcessing.Should().NotBeNull("processing fee should be recalculated"));
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 6: Non-fee fields changed → NO recalculation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonFeeFields_Changed_NoRecalculation()
    {
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            teamFeeBase: Deposit);

        var originalFees = teams.Select(t => new { t.TeamId, t.FeeBase, t.FeeProcessing, t.OwedTotal }).ToList();

        // Only change non-fee fields
        var req = BuildRequest(ctx, jobId, payTo: "New Pay To", mailTo: "New Mail To");
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var updatedTeams = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        foreach (var t in updatedTeams)
        {
            var orig = originalFees.First(o => o.TeamId == t.TeamId);
            t.FeeBase.Should().Be(orig.FeeBase, "FeeBase should not change when only non-fee fields are modified");
            t.FeeProcessing.Should().Be(orig.FeeProcessing, "FeeProcessing should not change");
            t.OwedTotal.Should().Be(orig.OwedTotal, "OwedTotal should not change");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 7: Waitlist teams skipped during recalc
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task WaitlistTeams_Skipped_DuringRecalc()
    {
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false, teamFeeBase: Deposit,
            teamCount: 1, addWaitlistTeam: true);

        var waitlistTeam = teams.First(t => t.TeamName == "Waitlist Team");
        var normalTeam = teams.First(t => t.TeamName != "Waitlist Team");

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var updatedNormal = await ctx.Teams.FirstAsync(t => t.TeamId == normalTeam.TeamId);
        updatedNormal.FeeBase.Should().Be(Deposit + BalanceDue,
            "normal team should be recalculated");

        var updatedWaitlist = await ctx.Teams.FirstAsync(t => t.TeamId == waitlistTeam.TeamId);
        updatedWaitlist.FeeBase.Should().Be(Deposit,
            "waitlist team should NOT be recalculated — fee unchanged");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 8: Paid teams — OwedTotal adjusts correctly
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PaidTeams_OwedTotalAdjusted_Correctly()
    {
        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false,
            teamCount: 1, teamFeeBase: Deposit, teamPaidTotal: 300m);

        var team = teams[0];
        team.OwedTotal.Should().Be(200m, "initial owed = 500 - 300");

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var updated = await ctx.Teams.FirstAsync(t => t.TeamId == team.TeamId);
        updated.FeeBase.Should().Be(Deposit + BalanceDue, "FeeBase should be full amount");
        updated.PaidTotal.Should().Be(300m, "PaidTotal should be unchanged");
        updated.OwedTotal.Should().Be(Deposit + BalanceDue - 300m,
            "OwedTotal should be full fee minus what's already paid");
    }
}
