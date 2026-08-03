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
using Xunit.Abstractions;

namespace TSIC.Tests.JobConfig;

/// <summary>
/// Tests that toggling fee-affecting flags in Job Config Payment tab
/// auto-recalculates team fees correctly.
///
/// Payment phase (deposit ↔ full pay) is NOT a Configure-Job concern anymore: the legacy
/// Jobs.B{Teams,Players}FullPaymentRequired columns are abandoned. Phase lives per-scope in
/// fees.JobFees.bFullPaymentRequired (set in the LADT editor, which runs its own scoped
/// reprice on save). The request phase fields still round-trip but are INERT — pinned here.
///
/// Each test prints BEFORE/AFTER fee tables so Ann can verify the numbers.
///
/// Scenarios cover:
///   - Phase fields in the request are inert (no entity write, no recalc)
///   - BAddProcessingFees ON/OFF
///   - Waitlist teams skipped during recalc
///   - Partially-paid teams adjust correctly (per-scope PIF stamp)
///   - Paid-in-full teams preserved via promotion on a phase downgrade
///   - Non-fee fields don't trigger recalc
/// </summary>
public class PaymentFeeRecalcTests
{
    private readonly ITestOutputHelper _out;

    private const decimal Deposit = 500m;
    private const decimal BalanceDue = 1500m;
    private const decimal ProcessingRate = 0.035m; // 3.5%

    public PaymentFeeRecalcTests(ITestOutputHelper output)
    {
        _out = output;
    }

    // ── Output Helpers ──────────────────────────────────────────────

    private void PrintScenario(string title, string description)
    {
        _out.WriteLine("");
        _out.WriteLine($"  ══════════════════════════════════════════════════");
        _out.WriteLine($"  SCENARIO: {title}");
        _out.WriteLine($"  {description}");
        _out.WriteLine($"  ══════════════════════════════════════════════════");
    }

    private void PrintJobConfig(string label, bool fullPayReq, bool addProcessing, bool procOnDeposit, decimal rate)
    {
        _out.WriteLine("");
        _out.WriteLine($"  Job Config ({label}):");
        _out.WriteLine($"    FullPayRequired:       {fullPayReq}");
        _out.WriteLine($"    AddProcessingFees:     {addProcessing}");
        _out.WriteLine($"    ProcessingOnDeposit:   {procOnDeposit}");
        _out.WriteLine($"    ProcessingRate:        {rate}%");
    }

    private void PrintTeamTable(string label, List<Teams> teams)
    {
        _out.WriteLine("");
        _out.WriteLine($"  {label}:");
        _out.WriteLine(string.Format("    {0,-20} {1,10} {2,12} {3,10} {4,10} {5,10}",
            "Team", "FeeBase", "Processing", "FeeTotal", "Paid", "Owed"));
        _out.WriteLine(string.Format("    {0,-20} {1,10} {2,12} {3,10} {4,10} {5,10}",
            "────────────────────", "──────────", "────────────", "──────────", "──────────", "──────────"));
        foreach (var t in teams)
        {
            _out.WriteLine(string.Format("    {0,-20} {1,10:C} {2,12:C} {3,10:C} {4,10:C} {5,10:C}",
                t.TeamName, t.FeeBase ?? 0, t.FeeProcessing ?? 0, t.FeeTotal ?? 0, t.PaidTotal ?? 0, t.OwedTotal ?? 0));
        }

        var totFeeBase = teams.Sum(t => t.FeeBase ?? 0);
        var totProc = teams.Sum(t => t.FeeProcessing ?? 0);
        var totTotal = teams.Sum(t => t.FeeTotal ?? 0);
        var totPaid = teams.Sum(t => t.PaidTotal ?? 0);
        var totOwed = teams.Sum(t => t.OwedTotal ?? 0);
        _out.WriteLine(string.Format("    {0,-20} {1,10:C} {2,12:C} {3,10:C} {4,10:C} {5,10:C}",
            "TOTALS", totFeeBase, totProc, totTotal, totPaid, totOwed));
    }

    private void PrintResult(string result)
    {
        _out.WriteLine("");
        _out.WriteLine($"  RESULT: {result}");
        _out.WriteLine("");
    }


    // ── Service Factory ─────────────────────────────────────────────

    /// <summary>
     /// Player-flag scenario factory: builds a job + JobConfigService and returns the
     /// Mock&lt;IPlayerRegistrationService&gt; so tests can verify whether
     /// <c>RecalculatePlayerFeesAsync</c> is invoked under the change-detection branch.
     /// </summary>
    private static async Task<(
        JobConfigService configService,
        SqlDbContext ctx,
        Guid jobId,
        Mock<IPlayerRegistrationService> playerRegMock)>
        CreatePlayerScenarioAsync(
            bool bPlayersFullPaymentRequired = false,
            bool bAddProcessingFees = false,
            decimal processingFeePercent = 3.5m,
            int recalcReturnCount = 0)
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        var job = builder.AddJob(
            processingFeePercent: processingFeePercent,
            bAddProcessingFees: bAddProcessingFees,
            bPlayersFullPaymentRequired: bPlayersFullPaymentRequired);

        await builder.SaveAsync();

        var configRepo = new JobConfigRepository(ctx);

        // Minimal team-side mocks: processing-rate change also triggers the team recalc
        // path (regardless of player flag), so we wire just enough to let that path
        // complete with zero teams. Nothing here affects the player-recalc verification.
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = false,
                BApplyProcessingFeesToTeamDeposit = false,
                PaymentMethodsAllowedCode = 1,
            });

        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsWithDetailsForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams>());

        var feeServiceMock = new Mock<IFeeResolutionService>();
        feeServiceMock.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingRate);

        var teamRegService = new TeamRegistrationService(
            new Mock<ILogger<TeamRegistrationService>>().Object,
            new Mock<IClubRepRepository>().Object,
            new Mock<IClubRepository>().Object,
            jobRepo.Object,
            new Mock<IJobLeagueRepository>().Object,
            new Mock<IAgeGroupRepository>().Object,
            teamRepo.Object,
            new Mock<IRegistrationRepository>().Object,
            new Mock<IUserRepository>().Object,
            new Mock<ITokenService>().Object,
            new Mock<TSIC.API.Services.Invites.IInviteTokenService>().Object,
            MockUserManager(),
            feeServiceMock.Object,
            new Mock<ITextSubstitutionService>().Object,
            new Mock<IEmailService>().Object,
            new Mock<IJobDiscountCodeRepository>().Object,
            new Mock<IClubTeamRepository>().Object,
            new Mock<ITeamPlacementService>().Object,
            new Mock<IPaymentStateService>().Object,
            new Mock<IRegisteredTeamShaper>().Object,
            CapabilityMocks.Open(),
            new Mock<IJobPaymentFeaturesService>().Object,
            new Mock<TSIC.API.Services.Teams.ITeamRenameService>().Object);

        var playerMock = new Mock<IPlayerRegistrationService>();
        playerMock
            .Setup(p => p.RecalculatePlayerFeesAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recalcReturnCount);

        var configService = new JobConfigService(
            configRepo,
            // Registration-readiness readout only — untouched by the fee-recalc paths under test.
            new Mock<IJobRepository>().Object,
            teamRegService,
            playerMock.Object,
            new ScheduleRepository(ctx),
            new Mock<TSIC.API.Services.Metadata.IProfileMetadataMigrationService>().Object,
            new Mock<ILogger<JobConfigService>>().Object);

        return (configService, ctx, job.JobId, playerMock);
    }

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
            bool addWaitlistTeam = false,
            bool? scopePhaseStamp = null)
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

        var configRepo = new JobConfigRepository(ctx);
        var teamRepo = new TeamRepository(ctx);

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) =>
            {
                var currentJob = ctx.Jobs.First(j => j.JobId == id);
                return Task.FromResult<JobFeeSettings?>(new JobFeeSettings
                {
                    BAddProcessingFees = currentJob.BAddProcessingFees,
                    BApplyProcessingFeesToTeamDeposit = currentJob.BApplyProcessingFeesToTeamDeposit,
                    PaymentMethodsAllowedCode = currentJob.PaymentMethodsAllowedCode,
                });
            });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProcessingRate);

        // The reprice engine batch-resolves the cascade up front and prices each team through
        // the pre-hydrated applier (mocked below). Return the same fixed Deposit/BalanceDue the
        // AccountingDataBuilder uses so the mocked applier prices teams exactly as production
        // would. scopePhaseStamp mirrors a fees.JobFees.bFullPaymentRequired stamp cascaded
        // into the resolved fee (the LADT per-scope phase — the only phase source now).
        feeService.Setup(f => f.ResolveFeesByTeamIdsAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Guid _, string _, IReadOnlyList<Guid> ids, CancellationToken _) =>
                Task.FromResult(ids.ToDictionary(
                    id => id,
                    _ => new TSIC.Contracts.Repositories.ResolvedFee
                    {
                        FeeConfigured = true,
                        Deposit = Deposit,
                        BalanceDue = BalanceDue,
                        BFullPaymentRequired = scopePhaseStamp,
                    })));

        feeService.Setup(f => f.ApplyTeamSwapFees(
                It.IsAny<Teams>(), It.IsAny<TSIC.Contracts.Repositories.ResolvedFee?>(),
                It.IsAny<TSIC.Contracts.Payments.PaymentState>(), It.IsAny<TeamFeeApplicationContext>()))
            .Callback((Teams t, TSIC.Contracts.Repositories.ResolvedFee? resolved,
                TSIC.Contracts.Payments.PaymentState _, TeamFeeApplicationContext feeCtx) =>
            {
                var deposit = resolved?.Deposit ?? 0;
                var balance = resolved?.BalanceDue ?? 0;

                // Mirror the real applier: phase comes from the per-scope stamp on the resolved
                // fee (ResolveFullPaymentPhase), and a team that has paid PAST the deposit is in
                // full-payment phase regardless, so a downgrade never re-stamps it DOWN to the
                // deposit (no bogus credit). Processing is OFF in the downgrade scenario, so
                // PaidTotal stands in for PrincipalPaid.
                var paidPastDeposit = (t.PaidTotal ?? 0m) > deposit + 0.01m;
                var fullPayment = TSIC.Contracts.Repositories.ResolvedFee.ResolveFullPaymentPhase(resolved) || paidPastDeposit;

                t.FeeBase = fullPayment ? deposit + balance : deposit;

                if (feeCtx.AddProcessingFees)
                {
                    t.FeeProcessing = feeCtx.ApplyProcessingFeesToDeposit
                        ? t.FeeBase * feeCtx.ProcessingFeePercent
                        : (fullPayment ? balance * feeCtx.ProcessingFeePercent : 0);
                }
                else
                {
                    t.FeeProcessing = 0;
                }

                t.FeeTotal = (t.FeeBase ?? 0) + (t.FeeProcessing ?? 0);
                t.OwedTotal = (t.FeeTotal ?? 0) - (t.PaidTotal ?? 0);
            });

        // The engine batch-hydrates PaymentStates up front; an empty batch dict + the shared
        // empty fallback replicates "no ledger rows" for every team (the mocked applier reads
        // PaidTotal off the entity instead).
        var paymentStateMock = new Mock<IPaymentStateService>();
        paymentStateMock.Setup(p => p.ForTeamsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, TSIC.Contracts.Payments.PaymentState>());
        paymentStateMock.Setup(p => p.ForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TSIC.Contracts.Payments.PaymentState.Empty(false, ProcessingRate, 0.015m));

        var teamRegService = new TeamRegistrationService(
            new Mock<ILogger<TeamRegistrationService>>().Object,
            new Mock<IClubRepRepository>().Object,
            new Mock<IClubRepository>().Object,
            jobRepo.Object,
            new Mock<IJobLeagueRepository>().Object,
            new Mock<IAgeGroupRepository>().Object,
            teamRepo,
            new RegistrationRepository(ctx),
            new Mock<IUserRepository>().Object,
            new Mock<ITokenService>().Object,
            new Mock<TSIC.API.Services.Invites.IInviteTokenService>().Object,
            MockUserManager(),
            feeService.Object,
            new Mock<ITextSubstitutionService>().Object,
            new Mock<IEmailService>().Object,
            new Mock<IJobDiscountCodeRepository>().Object,
            new Mock<IClubTeamRepository>().Object,
            new Mock<ITeamPlacementService>().Object,
            paymentStateMock.Object,
            new Mock<IRegisteredTeamShaper>().Object,
            CapabilityMocks.Open(),
            new Mock<IJobPaymentFeaturesService>().Object,
            new Mock<TSIC.API.Services.Teams.ITeamRenameService>().Object);

        var configService = new JobConfigService(
            configRepo,
            new Mock<IJobRepository>().Object,
            teamRegService,
            new Mock<IPlayerRegistrationService>().Object,
            new ScheduleRepository(ctx),
            new Mock<TSIC.API.Services.Metadata.IProfileMetadataMigrationService>().Object,
            new Mock<ILogger<JobConfigService>>().Object);

        return (configService, ctx, job.JobId, teams);
    }

    private static UpdateJobConfigPaymentRequest BuildRequest(
        SqlDbContext ctx, Guid jobId,
        bool? bTeamsFullPaymentRequired = null,
        bool? bAddProcessingFees = null,
        bool? bApplyProcessingFeesToTeamDeposit = null,
        decimal? processingFeePercent = null,
        string? payTo = null,
        string? mailTo = null,
        bool? bPlayersFullPaymentRequired = null,
        bool? bIncludePlayerDonation = null,
        bool? bIncludeTeamDonation = null)
    {
        var job = ctx.Jobs.First(j => j.JobId == jobId);
        return new UpdateJobConfigPaymentRequest
        {
            PaymentMethodsAllowedCode = job.PaymentMethodsAllowedCode,
            BAddProcessingFees = bAddProcessingFees ?? job.BAddProcessingFees,
            ProcessingFeePercent = processingFeePercent ?? job.ProcessingFeePercent,
            BEnableEcheck = job.BEnableEcheck,
            EcprocessingFeePercent = job.EcprocessingFeePercent,
            BApplyProcessingFeesToTeamDeposit = bApplyProcessingFeesToTeamDeposit ?? job.BApplyProcessingFeesToTeamDeposit,
            BTeamsFullPaymentRequired = bTeamsFullPaymentRequired ?? job.BTeamsFullPaymentRequired,
            BPlayersFullPaymentRequired = bPlayersFullPaymentRequired ?? job.BPlayersFullPaymentRequired,
            BIncludePlayerDonation = bIncludePlayerDonation ?? job.BIncludePlayerDonation,
            BIncludeTeamDonation = bIncludeTeamDonation ?? job.BIncludeTeamDonation,
            BAllowRefundsInPriorMonths = job.BAllowRefundsInPriorMonths,
            BAllowCreditAll = job.BAllowCreditAll,
            PlayerRegRefundPolicy = job.PlayerRegRefundPolicy,
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
    // TEST 1: Teams phase field in the payment request is INERT
    //   The legacy job column is abandoned — a payment-tab save that
    //   flips BTeamsFullPaymentRequired must neither write the entity
    //   nor trigger a team reprice. Phase flips live in the LADT
    //   editor (per-scope fees.JobFees.bFullPaymentRequired).
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TeamsPhaseField_InRequest_IsInert_NoWriteNoRecalc()
    {
        PrintScenario("Teams phase field: request flips ON → inert",
            "Payment-tab save sends BTeamsFullPaymentRequired=true. Column is abandoned: " +
            "no entity write, no team reprice — teams keep their current fees.");

        // Teams sit at FULL price with NO per-scope stamp — if any reprice ran off this save
        // it would re-stamp them down to the $500 deposit, so unchanged fees prove no recalc.
        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false, teamFeeBase: Deposit + BalanceDue);

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var after = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (must be identical)", after);

        var job = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        job.BTeamsFullPaymentRequired.Should().BeFalse("the abandoned phase column must never be written");
        foreach (var t in after)
        {
            t.FeeBase.Should().Be(Deposit + BalanceDue, "no reprice may run off the inert phase field");
            t.OwedTotal.Should().Be(Deposit + BalanceDue);
        }

        PrintResult("Entity flag untouched, no reprice triggered. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: Donation opt-in flags round-trip through Update + MapPayment
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DonationFlags_RoundTrip_ThroughUpdateAndMap()
    {
        PrintScenario("Donation opt-ins: OFF → ON",
            "Director enables optional player + team donations. Both persist to Jobs and read back through MapPayment.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false, teamFeeBase: Deposit);

        // Sanity: both default OFF on a fresh job.
        var before = await svc.GetFullConfigAsync(jobId, isSuperUser: false);
        before.Payment.BIncludePlayerDonation.Should().BeFalse();
        before.Payment.BIncludeTeamDonation.Should().BeFalse();

        var req = BuildRequest(ctx, jobId, bIncludePlayerDonation: true, bIncludeTeamDonation: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        // Write persisted to the entity (independent flags — both must flip)...
        var job = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        job.BIncludePlayerDonation.Should().BeTrue();
        job.BIncludeTeamDonation.Should().BeTrue();

        // ...and the read path (MapPayment) reflects both.
        var after = await svc.GetFullConfigAsync(jobId, isSuperUser: false);
        after.Payment.BIncludePlayerDonation.Should().BeTrue();
        after.Payment.BIncludeTeamDonation.Should().BeTrue();

        PrintResult("Both donation flags wrote to Jobs and round-tripped through MapPayment. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: SuperUser-only per-unit charges — a non-super save that omits
    // (sends null for) the gated fields MUST NOT null out existing values.
    // This guards the exact "hiding a field forces nulls into the record"
    // failure mode. A SuperUser save still writes them.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PerUnitCharges_NonSuperSaveWithNulls_PreservesExistingValues()
    {
        PrintScenario("Per-Unit Charges: non-super save must not wipe existing values",
            "Job has PerPlayer=$42. A non-super PUT sends nulls (fields hidden). Values must survive.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false, teamFeeBase: Deposit);

        // Establish existing per-unit charges on the record.
        var seeded = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        seeded.PerPlayerCharge = 42m;
        seeded.PerTeamCharge = 250m;
        seeded.PerMonthCharge = 9m;
        await ctx.SaveChangesAsync();

        // Non-super PUT with the gated fields nulled (as a hidden-section payload would arrive).
        var nullReq = BuildRequest(ctx, jobId) with
        {
            PerPlayerCharge = null,
            PerTeamCharge = null,
            PerMonthCharge = null,
        };
        await svc.UpdatePaymentAsync(jobId, nullReq, isSuperUser: false);

        var afterNonSuper = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        afterNonSuper.PerPlayerCharge.Should().Be(42m, "a non-super save must not overwrite gated values with null");
        afterNonSuper.PerTeamCharge.Should().Be(250m);
        afterNonSuper.PerMonthCharge.Should().Be(9m);

        // And the read path nulls them out for the non-super (visibility gate), without touching the DB.
        var read = await svc.GetFullConfigAsync(jobId, isSuperUser: false);
        read.Payment.PerPlayerCharge.Should().BeNull("non-super read is gated to null");

        // A SuperUser save still writes them.
        var superReq = BuildRequest(ctx, jobId) with { PerPlayerCharge = 99m };
        await svc.UpdatePaymentAsync(jobId, superReq, isSuperUser: true);
        var afterSuper = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        afterSuper.PerPlayerCharge.Should().Be(99m, "a SuperUser save writes the gated value");

        PrintResult("Non-super null save preserved $42/$250/$9; SuperUser save wrote $99. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 5: Processing Fees OFF → removes processing from teams
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessingFees_TurnedOff_RemovesProcessingFromTeams()
    {
        var existingProcessing = Deposit * ProcessingRate;

        PrintScenario("Processing Fees: ON → OFF",
            $"Director disables CC fees. Processing ({existingProcessing:C}) removed from all teams.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bAddProcessingFees: true, bApplyProcessingFeesToTeamDeposit: true,
            teamFeeBase: Deposit, teamFeeProcessing: existingProcessing);

        PrintJobConfig("BEFORE", fullPayReq: false, addProcessing: true, procOnDeposit: true, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, bAddProcessingFees: false);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: false, addProcessing: false, procOnDeposit: true, rate: 3.5m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER", after);

        foreach (var t in after)
        {
            t.FeeProcessing.Should().Be(0);
        }

        PrintResult("Processing removed. FeeProcessing = $0. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 6: Non-fee fields changed → NO recalculation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonFeeFields_Changed_NoRecalculation()
    {
        PrintScenario("Non-Fee Fields Only",
            "Director changes PayTo and MailTo. No fee fields touched — teams should be unchanged.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(teamFeeBase: Deposit);

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, payTo: "New Pay To", mailTo: "New Mail To");
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (should be identical)", after);

        foreach (var t in after)
        {
            var orig = before.First(o => o.TeamId == t.TeamId);
            t.FeeBase.Should().Be(orig.FeeBase);
            t.FeeProcessing.Should().Be(orig.FeeProcessing);
            t.OwedTotal.Should().Be(orig.OwedTotal);
        }

        PrintResult("All fees unchanged. No recalculation triggered. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 7: Waitlist teams skipped during recalc
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task WaitlistTeams_Skipped_DuringRecalc()
    {
        PrintScenario("Waitlist Teams Skipped",
            "1 normal team + 1 WAITLIST team, per-scope PIF stamp ON. A rate change triggers " +
            "the reprice; only the normal team should be recalculated.");

        // Phase now rides on the per-scope stamp; the recalc trigger is a still-live
        // fee-affecting field (processing rate), not the abandoned job flag.
        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            teamFeeBase: Deposit, teamCount: 1, addWaitlistTeam: true,
            scopePhaseStamp: true);

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, processingFeePercent: 3.99m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER", after);

        var normal = after.First(t => t.TeamName == "Team 1");
        var waitlist = after.First(t => t.TeamName == "Waitlist Team");

        normal.FeeBase.Should().Be(Deposit + BalanceDue);
        waitlist.FeeBase.Should().Be(Deposit, "waitlist team should NOT be recalculated");

        PrintResult($"Team 1: $500 → $2,000 (recalculated). Waitlist Team: $500 → $500 (skipped). PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 8: Partially-paid team — OwedTotal adjusts correctly
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PaidTeams_OwedTotalAdjusted_Correctly()
    {
        PrintScenario("Partially Paid Team",
            "Team has paid $300 of $500 deposit; per-scope PIF stamp ON ($2,000 full). " +
            "A rate change triggers the reprice. Owed should be $2,000 - $300 = $1,700. " +
            "PaidTotal stays at $300.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            teamCount: 1, teamFeeBase: Deposit, teamPaidTotal: 300m,
            scopePhaseStamp: true);

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE (paid $300 of $500 deposit)", before);

        var req = BuildRequest(ctx, jobId, processingFeePercent: 3.99m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (repriced under PIF stamp)", after);

        var updated = after[0];
        updated.FeeBase.Should().Be(Deposit + BalanceDue);
        updated.PaidTotal.Should().Be(300m);
        updated.OwedTotal.Should().Be(Deposit + BalanceDue - 300m);

        PrintResult($"FeeBase: $500 → $2,000. PaidTotal: $300 (unchanged). Owed: $200 → $1,700. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 9: Processing Rate Changed → recalculates
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessingRate_Changed_RecalculatesProcessingFees()
    {
        PrintScenario("Processing Rate Changed: 3.5% → 3.99%",
            "Director changes processing fee rate within allowed [3.5, 4.0] range. Teams recalculated.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bAddProcessingFees: true, bApplyProcessingFeesToTeamDeposit: true,
            processingFeePercent: 3.5m,
            teamFeeBase: Deposit, teamFeeProcessing: Deposit * ProcessingRate);

        PrintJobConfig("BEFORE", fullPayReq: false, addProcessing: true, procOnDeposit: true, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, processingFeePercent: 3.99m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: false, addProcessing: true, procOnDeposit: true, rate: 3.99m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER", after);

        after.Should().AllSatisfy(t =>
            t.FeeProcessing.Should().NotBeNull("processing fee should be recalculated"));

        PrintResult("Recalculation triggered by rate change. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 10: Paid-in-full team preserved on a phase DOWNGRADE
    //   The downgrade now arrives as a reprice with NO per-scope stamp
    //   (director cleared the PIF stamp in LADT; here the reprice is
    //   triggered by a rate change against an unstamped scope). PIF team
    //   must NOT be re-stamped to deposit-only — that would shrink
    //   FeeTotal below PaidTotal and produce a negative OwedTotal
    //   (bogus credit) that propagates into the rep's pulse balance.
    //   Guaranteed by the applier's paid-past-deposit promotion
    //   (ApplyTeamSwapFeesAsync), not an engine-level skip — a paid-ahead
    //   team stays at full price on the downgrade. Mirrors the player path.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PaidInFullTeam_PreservedViaPromotion_OnPhaseDowngrade()
    {
        var fullAmount = Deposit + BalanceDue; // 2000

        PrintScenario("Paid-In-Full Team Preserved: phase downgrade (no per-scope stamp)",
            "1 PIF team (paid $2,000 of $2,000) + 1 unpaid team, both stamped at full price. " +
            "Reprice runs with no PIF stamp (deposit phase). PIF team must be left untouched; " +
            "unpaid team reverts to deposit-only. " +
            "Rep registration row must re-aggregate to match the new team sums.");

        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            teamCount: 2,
            teamFeeBase: fullAmount,
            teamPaidTotal: 0m); // builder default; we'll patch one team to PIF below

        // Patch first team to be paid-in-full (FeeTotal=$2000, PaidTotal=$2000, Owed=$0).
        var pifTeam = teams[0];
        pifTeam.PaidTotal = fullAmount;
        pifTeam.FeeTotal = fullAmount;
        pifTeam.OwedTotal = 0m;

        // Seed the rep registration with the matching pre-recalc aggregate so we can
        // prove the post-recalc sync re-aggregates correctly (not just "happened to
        // start at zero").
        var repBefore = await ctx.Registrations
            .FirstAsync(r => r.JobId == jobId && r.ClubName == "Test Club");
        repBefore.FeeBase = fullAmount * 2;        // $4,000 (team 1 + team 2)
        repBefore.FeeTotal = fullAmount * 2;       // $4,000
        repBefore.PaidTotal = fullAmount;          // $2,000 (only team 1 paid)
        repBefore.OwedTotal = fullAmount;          // $2,000 (only team 2 owes)
        await ctx.SaveChangesAsync();

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE (Team 1 = PIF, Team 2 = unpaid full-pay)", before);

        var req = BuildRequest(ctx, jobId, processingFeePercent: 3.99m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var after = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (PIF preserved, unpaid reverted to deposit)", after);

        var pifAfter = after.First(t => t.TeamId == pifTeam.TeamId);
        var otherAfter = after.First(t => t.TeamId != pifTeam.TeamId);

        // PIF team: untouched. FeeBase + PaidTotal + OwedTotal preserved.
        pifAfter.FeeBase.Should().Be(fullAmount, "PIF team must not be re-stamped");
        pifAfter.PaidTotal.Should().Be(fullAmount);
        pifAfter.OwedTotal.Should().Be(0m, "PIF team must not develop a bogus credit");

        // Unpaid team: reverted to deposit-only.
        otherAfter.FeeBase.Should().Be(Deposit, "non-PIF team should revert");
        otherAfter.OwedTotal.Should().Be(Deposit);

        // Live SUM of team OwedTotal — must equal $500, not -$1500.
        var teamSumOwed = after.Sum(t => t.OwedTotal ?? 0m);
        teamSumOwed.Should().Be(Deposit,
            "team-row aggregate must be positive — the paid-ahead team stays at full price via promotion");

        // Rep registration row MUST be re-synced to match the new team aggregates.
        // SynchronizeClubRepFinancialsAsync call inside RecalculateTeamFeesAsync is
        // what guarantees this. Without that call, the rep row stays at the seeded
        // pre-recalc state ($4,000 / $2,000 / $2,000) while teams hold the new totals
        // ($2,500 / $2,000 / $500) — a silent drift that downstream callers
        // (TeamSearchService balance-due gate, CC charge limits) read and act on.
        var repAfter = await ctx.Registrations.AsNoTracking()
            .FirstAsync(r => r.RegistrationId == repBefore.RegistrationId);
        var expectedFeeBase = fullAmount + Deposit;       // $2,500
        var expectedFeeTotal = expectedFeeBase;           // no processing in this scenario
        var expectedPaidTotal = fullAmount;               // unchanged ($2,000)
        var expectedOwedTotal = Deposit;                  // $500 from team 2

        repAfter.FeeBase.Should().Be(expectedFeeBase, "rep.FeeBase must equal SUM(team.FeeBase)");
        repAfter.FeeTotal.Should().Be(expectedFeeTotal, "rep.FeeTotal must equal SUM(team.FeeTotal)");
        repAfter.PaidTotal.Should().Be(expectedPaidTotal, "rep.PaidTotal must equal SUM(team.PaidTotal)");
        repAfter.OwedTotal.Should().Be(expectedOwedTotal, "rep.OwedTotal must equal SUM(team.OwedTotal)");

        _out.WriteLine("");
        _out.WriteLine($"  REP REGISTRATION SYNC:");
        _out.WriteLine($"    FeeBase:    ${repBefore.FeeBase} → ${repAfter.FeeBase} (expected ${expectedFeeBase})");
        _out.WriteLine($"    FeeTotal:   ${repBefore.FeeTotal} → ${repAfter.FeeTotal} (expected ${expectedFeeTotal})");
        _out.WriteLine($"    PaidTotal:  ${repBefore.PaidTotal} → ${repAfter.PaidTotal} (expected ${expectedPaidTotal})");
        _out.WriteLine($"    OwedTotal:  ${repBefore.OwedTotal} → ${repAfter.OwedTotal} (expected ${expectedOwedTotal})");

        PrintResult($"PIF team preserved. Unpaid team reverted. Rep registration re-synced. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 10b: Owed-zeroed deposit team reprices under a per-scope PIF stamp
    //   The production bug. A deposit-phase team whose owed was driven to $0
    //   (a recorded deposit, or a client correction) must NOT be treated as
    //   paid-in-full and skipped — the engine no longer carries an
    //   OwedTotal<=0 gate, so a reprice under the PIF stamp re-stamps it to
    //   full price and it correctly owes the balance.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task OwedZeroedDepositTeam_RepricedToFullPrice_UnderPifStamp()
    {
        PrintScenario("Owed-Zeroed Deposit Team Reprices Under PIF Stamp",
            "Deposit-phase team paid its $500 deposit (owed $0). Scope carries a PIF stamp; " +
            "a rate change triggers the reprice. " +
            "Team must re-stamp to $2,000 and owe $1,500 — NOT be skipped as paid-in-full.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            teamCount: 1, teamFeeBase: Deposit, teamPaidTotal: Deposit, // owed = $0 in deposit phase
            scopePhaseStamp: true);

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        before[0].OwedTotal.Should().Be(0m, "precondition: deposit fully paid → owed $0");
        PrintTeamTable("BEFORE (deposit paid, owed $0)", before);

        var req = BuildRequest(ctx, jobId, processingFeePercent: 3.99m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (repriced under PIF stamp)", after);

        var updated = after[0];
        updated.FeeBase.Should().Be(Deposit + BalanceDue, "owed-zeroed team must still re-stamp to full price");
        updated.PaidTotal.Should().Be(Deposit, "the $500 already paid is preserved");
        updated.OwedTotal.Should().Be(BalanceDue, "2000 full − 500 paid = 1500 now owed");

        PrintResult($"FeeBase: $500 → $2,000. Owed: $0 → $1,500 (no longer skipped). PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // PLAYER-SIDE TESTS — verify the player-recalc trigger contract in
    // JobConfigService.UpdatePaymentAsync: processing-fee config changes
    // invoke IPlayerRegistrationService.RecalculatePlayerFeesAsync; the
    // abandoned BPlayersFullPaymentRequired request field is inert.
    //
    // Fee correctness for the recalc itself is owned by
    // FeeResolutionService unit tests; these verify the trigger contract.
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    // PLAYER 1: Players phase field in the request is INERT
    //   (no entity write, no player recalc — phase lives per-scope)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PlayersPhaseField_InRequest_IsInert_NoWriteNoRecalc()
    {
        PrintScenario("Players phase field: request flips ON → inert",
            "Payment-tab save sends BPlayersFullPaymentRequired=true. Column is abandoned: " +
            "no entity write, RecalculatePlayerFeesAsync must NOT be invoked.");

        var (svc, ctx, jobId, playerMock) = await CreatePlayerScenarioAsync(
            bPlayersFullPaymentRequired: false);

        var req = BuildRequest(ctx, jobId, bPlayersFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        playerMock.Verify(p => p.RecalculatePlayerFeesAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);

        var job = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == jobId);
        job.BPlayersFullPaymentRequired.Should().BeFalse("the abandoned phase column must never be written");

        PrintResult("Entity flag untouched, no player recalc triggered. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // PLAYER 3: Processing-rate change ALSO triggers player recalc
    //   (FeeProcessing depends on rate; players need re-stamping too.)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PlayersFlagUnchanged_ProcessingRateChanged_TriggersPlayerRecalc()
    {
        PrintScenario("Players Flag Unchanged, Processing Rate Changed: 3.5% → 3.99%",
            "Player phase flag stays the same; rate changes within allowed [3.5, 4.0]. " +
            "Player FeeProcessing depends on rate, so RecalculatePlayerFeesAsync must run.");

        var (svc, ctx, jobId, playerMock) = await CreatePlayerScenarioAsync(
            bPlayersFullPaymentRequired: false,
            bAddProcessingFees: true,
            processingFeePercent: 3.5m);

        var req = BuildRequest(ctx, jobId, processingFeePercent: 3.99m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        playerMock.Verify(p => p.RecalculatePlayerFeesAsync(
            jobId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);

        PrintResult("Player recalc triggered by rate change. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // PLAYER 4: Non-fee fields changed → player recalc NOT called
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PlayersNonFeeFields_Changed_PlayerRecalcNotCalled()
    {
        PrintScenario("Players Non-Fee Fields Only",
            "Director changes PayTo/MailTo. No player-fee-affecting fields. " +
            "RecalculatePlayerFeesAsync must NOT be invoked.");

        var (svc, ctx, jobId, playerMock) = await CreatePlayerScenarioAsync(
            bPlayersFullPaymentRequired: false);

        var req = BuildRequest(ctx, jobId, payTo: "New Pay To", mailTo: "New Mail To");
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        playerMock.Verify(p => p.RecalculatePlayerFeesAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);

        PrintResult("Player recalc skipped when no fee-affecting fields change. PASS");
    }
}
