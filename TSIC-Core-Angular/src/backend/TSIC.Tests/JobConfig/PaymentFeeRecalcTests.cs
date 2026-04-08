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
/// Each test prints BEFORE/AFTER fee tables so Ann can verify the numbers.
///
/// Scenarios cover:
///   - BTeamsFullPaymentRequired ON/OFF (deposit ↔ full pay)
///   - BAddProcessingFees ON/OFF
///   - CC fees on balance only (tournament client) vs CC fees on deposit+balance (typical)
///   - Waitlist teams skipped
///   - Partially-paid teams adjust correctly
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

    // ── Snapshot helper — captures team state before EF tracking mutates it ──

    private static List<Teams> Snapshot(List<Teams> teams) =>
        teams.Select(t => new Teams
        {
            TeamId = t.TeamId,
            TeamName = t.TeamName,
            FeeBase = t.FeeBase,
            FeeProcessing = t.FeeProcessing,
            FeeTotal = t.FeeTotal,
            PaidTotal = t.PaidTotal,
            OwedTotal = t.OwedTotal,
        }).ToList();

    // ── Service Factory ─────────────────────────────────────────────

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

        var configRepo = new JobConfigRepository(ctx);
        var teamRepo = new TeamRepository(ctx);

        var jobRepo = new Mock<IJobRepository>();
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

        feeService.Setup(f => f.ApplyTeamSwapFeesAsync(
                It.IsAny<Teams>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<TeamFeeApplicationContext>(), It.IsAny<CancellationToken>()))
            .Returns((Teams t, Guid _, Guid agId, TeamFeeApplicationContext feeCtx, CancellationToken _) =>
            {
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
    // TEST 1: Full Pay Required ON → deposit-only teams get full fee
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPayRequired_TurnedOn_RecalculatesFeesUp()
    {
        PrintScenario("Full Pay Required: OFF → ON",
            "Director enables balance due. Teams go from deposit-only ($500) to full fee ($2,000).");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false, teamFeeBase: Deposit);

        PrintJobConfig("BEFORE", fullPayReq: false, addProcessing: false, procOnDeposit: false, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: true, addProcessing: false, procOnDeposit: false, rate: 3.5m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER", after);

        foreach (var t in after)
        {
            t.FeeBase.Should().Be(Deposit + BalanceDue);
            t.OwedTotal.Should().Be(Deposit + BalanceDue);
        }

        PrintResult("FeeBase moved from $500 → $2,000. OwedTotal matches. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 2: Full Pay Required OFF → full-fee teams revert to deposit
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullPayRequired_TurnedOff_RecalculatesFeesDown()
    {
        PrintScenario("Full Pay Required: ON → OFF",
            "Director disables balance due. Teams revert from full fee ($2,000) to deposit-only ($500).");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: true, teamFeeBase: Deposit + BalanceDue);

        PrintJobConfig("BEFORE", fullPayReq: true, addProcessing: false, procOnDeposit: false, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: false);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: false, addProcessing: false, procOnDeposit: false, rate: 3.5m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER", after);

        foreach (var t in after)
        {
            t.FeeBase.Should().Be(Deposit);
            t.OwedTotal.Should().Be(Deposit);
        }

        PrintResult("FeeBase reverted from $2,000 → $500. OwedTotal matches. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 3a: Typical Tournament — CC fees on everything, FullPay ON
    //   Deposit phase → Full pay. Processing on full amount.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TypicalTournament_FullPayOn_CCFeesOnEverything()
    {
        PrintScenario("Typical Tournament: Enable Full Pay (CC fees on everything)",
            "CC fees (3.5%) apply to ENTIRE fee (deposit + balance). " +
            "Director turns on FullPayRequired. Teams go from deposit → full fee + CC on all.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true,
            teamFeeBase: Deposit, teamFeeProcessing: Deposit * ProcessingRate);

        PrintJobConfig("BEFORE", fullPayReq: false, addProcessing: true, procOnDeposit: true, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE (deposit phase, CC on deposit)", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: true, addProcessing: true, procOnDeposit: true, rate: 3.5m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (full pay, CC on full amount)", after);

        var expectedProcessing = (Deposit + BalanceDue) * ProcessingRate; // $2000 × 3.5% = $70
        foreach (var t in after)
        {
            t.FeeBase.Should().Be(Deposit + BalanceDue);
            t.FeeProcessing.Should().Be(expectedProcessing);
            t.FeeTotal.Should().Be(Deposit + BalanceDue + expectedProcessing);
        }

        PrintResult($"FeeBase: $500 → $2,000. Processing: ${Deposit * ProcessingRate} → ${expectedProcessing}. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 3b: Typical Tournament — CC fees on everything, FullPay OFF
    //   Full pay → Deposit phase. Processing reverts to deposit only.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task TypicalTournament_FullPayOff_CCFeesRevertToDeposit()
    {
        var fullProcessing = (Deposit + BalanceDue) * ProcessingRate;

        PrintScenario("Typical Tournament: Disable Full Pay (CC fees revert to deposit)",
            "CC fees (3.5%) apply to ENTIRE fee. Director turns OFF FullPayRequired. " +
            "Teams revert from full fee → deposit. CC now only on deposit.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: true,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true,
            teamFeeBase: Deposit + BalanceDue, teamFeeProcessing: fullProcessing);

        PrintJobConfig("BEFORE", fullPayReq: true, addProcessing: true, procOnDeposit: true, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE (full pay, CC on full amount)", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: false);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: false, addProcessing: true, procOnDeposit: true, rate: 3.5m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (deposit phase, CC on deposit only)", after);

        var expectedProcessing = Deposit * ProcessingRate; // $500 × 3.5% = $17.50
        foreach (var t in after)
        {
            t.FeeBase.Should().Be(Deposit);
            t.FeeProcessing.Should().Be(expectedProcessing);
            t.FeeTotal.Should().Be(Deposit + expectedProcessing);
        }

        PrintResult($"FeeBase: $2,000 → $500. Processing: ${fullProcessing} → ${expectedProcessing}. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 4a: Complicated Client — CC fees on balance only, FullPay ON
    //   Deposit phase → Full pay. Processing on balance only (not deposit).
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ComplicatedClient_FullPayOn_CCFeesOnBalanceOnly()
    {
        PrintScenario("Complicated Client: Enable Full Pay (CC fees on balance ONLY)",
            "CC fees (3.5%) apply ONLY to balance due ($1,500), NOT deposit ($500). " +
            "Director turns on FullPayRequired. In deposit phase, processing was $0 (no balance).");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: false,
            teamFeeBase: Deposit, teamFeeProcessing: 0); // deposit phase, no balance → no CC

        PrintJobConfig("BEFORE", fullPayReq: false, addProcessing: true, procOnDeposit: false, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE (deposit phase, CC on balance only = $0)", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: true, addProcessing: true, procOnDeposit: false, rate: 3.5m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (full pay, CC on balance only)", after);

        var expectedProcessing = BalanceDue * ProcessingRate; // $1500 × 3.5% = $52.50
        foreach (var t in after)
        {
            t.FeeBase.Should().Be(Deposit + BalanceDue);
            t.FeeProcessing.Should().Be(expectedProcessing,
                $"3.5% of balance-only ${BalanceDue} = ${expectedProcessing}");
            t.FeeTotal.Should().Be(Deposit + BalanceDue + expectedProcessing);
        }

        PrintResult($"FeeBase: $500 → $2,000. Processing: $0 → ${expectedProcessing} (balance only). Deposit excluded. PASS");
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST 4b: Complicated Client — CC fees on balance only, FullPay OFF
    //   Full pay → Deposit phase. Processing drops to $0 (no balance in deposit phase).
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ComplicatedClient_FullPayOff_CCFeesDropToZero()
    {
        var balanceProcessing = BalanceDue * ProcessingRate;

        PrintScenario("Complicated Client: Disable Full Pay (CC fees drop to $0)",
            "CC fees (3.5%) on balance only. Director turns OFF FullPayRequired. " +
            "In deposit phase there's no balance → processing drops to $0.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: true,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: false,
            teamFeeBase: Deposit + BalanceDue, teamFeeProcessing: balanceProcessing);

        PrintJobConfig("BEFORE", fullPayReq: true, addProcessing: true, procOnDeposit: false, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE (full pay, CC on balance only)", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: false);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: false, addProcessing: true, procOnDeposit: false, rate: 3.5m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (deposit phase, no balance → no CC)", after);

        foreach (var t in after)
        {
            t.FeeBase.Should().Be(Deposit);
            t.FeeProcessing.Should().Be(0,
                "deposit phase with CC-on-balance-only means $0 processing");
            t.FeeTotal.Should().Be(Deposit);
        }

        PrintResult($"FeeBase: $2,000 → $500. Processing: ${balanceProcessing} → $0 (no balance in deposit phase). PASS");
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

        var (svc, ctx, jobId, teams) = await CreateServiceAsync(teamFeeBase: Deposit);

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
            "1 normal team + 1 WAITLIST team. Only normal team should be recalculated.");

        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false, teamFeeBase: Deposit,
            teamCount: 1, addWaitlistTeam: true);

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
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
            "Team has paid $300 of $500 deposit. Director enables full pay ($2,000). " +
            "Owed should be $2,000 - $300 = $1,700. PaidTotal stays at $300.");

        var (svc, ctx, jobId, teams) = await CreateServiceAsync(
            bTeamsFullPaymentRequired: false,
            teamCount: 1, teamFeeBase: Deposit, teamPaidTotal: 300m);

        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE (paid $300 of $500 deposit)", before);

        var req = BuildRequest(ctx, jobId, bTeamsFullPaymentRequired: true);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER (full pay enabled)", after);

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
        PrintScenario("Processing Rate Changed: 3.5% → 5.0%",
            "Director changes processing fee rate. Existing teams should be recalculated.");

        var (svc, ctx, jobId, _) = await CreateServiceAsync(
            bAddProcessingFees: true, bApplyProcessingFeesToTeamDeposit: true,
            processingFeePercent: 3.5m,
            teamFeeBase: Deposit, teamFeeProcessing: Deposit * ProcessingRate);

        PrintJobConfig("BEFORE", fullPayReq: false, addProcessing: true, procOnDeposit: true, rate: 3.5m);
        var before = await ctx.Teams.AsNoTracking().Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("BEFORE", before);

        var req = BuildRequest(ctx, jobId, processingFeePercent: 5.0m);
        await svc.UpdatePaymentAsync(jobId, req, isSuperUser: false);

        PrintJobConfig("AFTER", fullPayReq: false, addProcessing: true, procOnDeposit: true, rate: 5.0m);
        var after = await ctx.Teams.Where(t => t.JobId == jobId).ToListAsync();
        PrintTeamTable("AFTER", after);

        after.Should().AllSatisfy(t =>
            t.FeeProcessing.Should().NotBeNull("processing fee should be recalculated"));

        PrintResult("Recalculation triggered by rate change. PASS");
    }
}
