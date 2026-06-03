using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using TSIC.API.Controllers;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Jobs;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.PlayerRegistration.DiscountCode;

/// <summary>
/// DISCOUNT CODE TESTS
///
/// These tests validate the ApplyDiscount action on PlayerRegistrationPaymentController.
/// Covers absolute and percent discounts, multi-player application, processing fee
/// reduction, full (100%) waivers, capping, and rejection paths.
///
/// A discount is a FEE MODIFIER, not a payment: it reduces FeeTotal and is recorded on the
/// registration (DiscountCodeId — the canonical redemption-count key), and it NEVER writes a
/// RegistrationAccounting row or touches PaidTotal. A full (100%) discount therefore zeroes the
/// balance honestly (FeeTotal 0, PaidTotal unchanged, OwedTotal 0) — no fake "Correction" payment.
///
/// Each test uses real repositories against an in-memory database.
/// Only IJobLookupService, IPaymentService, IJobRepository, IFeeResolutionService, and ILogger are mocked.
///
/// IMPORTANT — discount base &amp; processing fee:
/// The discount is computed from the registration's FeeBase (never the client-submitted item
/// amount, which is the proc-inclusive owed balance). Percent = FeeBase x pct; fixed = min(code,
/// FeeBase). After ReduceProcessingFee adjusts the proc, the controller passes the adjusted
/// FeeProcessing directly to FeeMath (0 included), so a full discount that legitimately
/// zeroes the proc is NOT re-inflated, and a no-proc job keeps proc at 0.
/// </summary>
public class DiscountCodeTests
{
    private const string FamilyUserId = "test-family-user-001";
    private const string JobPath = "test-job-dc";
    private static readonly Guid JobId = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000001");

    private static int _discountCodeAiCounter = 100;

    private static async Task<(PlayerRegistrationPaymentController controller, SqlDbContext ctx, AccountingDataBuilder builder)>
        CreateControllerAsync(
            decimal processingFeePercent = 3.5m,
            bool bAddProcessingFees = false)
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        ctx.Jobs.Add(new Jobs
        {
            JobId = JobId,
            JobPath = JobPath,
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "Club Rep",
            ProcessingFeePercent = processingFeePercent,
            BAddProcessingFees = bAddProcessingFees,
            Modified = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var registrationRepo = new RegistrationRepository(ctx);
        var discountCodeRepo = new JobDiscountCodeRepository(ctx);

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = bAddProcessingFees,
                BApplyProcessingFeesToTeamDeposit = false,
                BTeamsFullPaymentRequired = false,
                PaymentMethodsAllowedCode = 7
            });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingFeePercent / 100m);

        var feeAdjustment = new RegistrationFeeAdjustmentService(jobRepo.Object, feeService.Object);

        var jobLookup = new Mock<IJobLookupService>();
        jobLookup.Setup(j => j.GetJobIdByPathAsync(JobPath))
            .ReturnsAsync(JobId);

        var paymentService = new Mock<IPaymentService>();
        var paymentState = new Mock<IPaymentStateService>();
        paymentState.Setup(p => p.ForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TSIC.Contracts.Payments.PaymentState.Empty(bAddProcessingFees, processingFeePercent / 100m, 0.015m));
        var logger = new Mock<ILogger<PlayerRegistrationPaymentController>>();

        var controller = new PlayerRegistrationPaymentController(
            jobLookup.Object,
            paymentService.Object,
            discountCodeRepo,
            registrationRepo,
            feeAdjustment,
            paymentState.Object,
            logger.Object);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, FamilyUserId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return (controller, ctx, builder);
    }

    /// <summary>Seeds a discount code and returns its Ai — the value the controller stamps onto
    /// Registrations.DiscountCodeId on redemption (the canonical redemption-count key read by
    /// JobDiscountCodeRepository.GetUsageCountAsync).</summary>
    private static int AddDiscountCode(
        SqlDbContext ctx,
        decimal codeAmount,
        bool bAsPercent = false,
        string codeName = "SAVE100")
    {
        var ai = Interlocked.Increment(ref _discountCodeAiCounter);
        var code = new JobDiscountCodes
        {
            Ai = ai,
            JobId = JobId,
            CodeName = codeName,
            BAsPercent = bAsPercent,
            CodeAmount = codeAmount,
            Active = true,
            CodeStartDate = DateTime.UtcNow.AddDays(-1),
            CodeEndDate = DateTime.UtcNow.AddDays(30),
            LebUserId = "seed",
            Modified = DateTime.UtcNow
        };
        ctx.JobDiscountCodes.Add(code);
        return ai;
    }

    private static Registrations AddRegistration(
        SqlDbContext ctx,
        string userId,
        decimal feeBase,
        decimal feeProcessing = 0m,
        decimal feeDiscount = 0m,
        decimal paidTotal = 0m,
        string? insuredName = null)
    {
        var feeTotal = feeBase + feeProcessing - feeDiscount;
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = JobId,
            UserId = userId,
            FamilyUserId = FamilyUserId,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = feeDiscount,
            FeeDonation = 0m,
            FeeLatefee = 0m,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = Math.Max(0m, feeTotal - paidTotal),
            BActive = true,
            InsuredName = insuredName ?? "Test Player",
            Modified = DateTime.UtcNow
        };
        ctx.Registrations.Add(reg);
        return reg;
    }

    private static ApplyDiscountRequestDto MakeRequest(string code, params (string playerId, decimal amount)[] items)
    {
        return new ApplyDiscountRequestDto
        {
            JobPath = JobPath,
            Code = code,
            Items = items.Select(i => new ApplyDiscountItemDto { PlayerId = i.playerId, Amount = i.amount }).ToList()
        };
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Absolute partial discount with processing fees
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $100 off $595 (base=$595 + 3.5% proc) → proc reduced, total=$512.33")]
    public async Task Absolute_PartialDiscount_WithProcessingFees()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        var ai = AddDiscountCode(ctx, codeAmount: 100m, codeName: "SAVE100");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 595m, feeProcessing: 20.83m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("SAVE100", (playerId, 615.83m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(100m);
        dto.SuccessCount.Should().Be(1);

        // Fixed $100 (< FeeBase 595). ReduceProcessingFee: 100 * 0.035 = 3.50, proc 20.83 → 17.33.
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(100m);
        dbReg.FeeProcessing.Should().Be(17.33m);
        dbReg.FeeTotal.Should().Be(512.33m);
        dbReg.OwedTotal.Should().Be(512.33m);
        dbReg.PaidTotal.Should().Be(0m);
        dbReg.DiscountCodeId.Should().Be(ai);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Absolute partial discount — no-proc job → no processing fee invented
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $100 off $595 (no-proc job) → no proc added, total=$495")]
    public async Task Absolute_PartialDiscount_NoProcessingOnReg()
    {
        var (controller, ctx, _) = await CreateControllerAsync(bAddProcessingFees: false);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 100m, codeName: "SAVE100");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 595m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("SAVE100", (playerId, 595m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(100m);

        // No-proc job: ReduceProcessingFee is a no-op and the adjusted proc (0) flows straight to
        // FeeMath, so no phantom processing fee is invented. total = 595 - 100 = 495.
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(100m);
        dbReg.FeeProcessing.Should().Be(0m);
        dbReg.FeeTotal.Should().Be(495m);
        dbReg.OwedTotal.Should().Be(495m);
        dbReg.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. Fixed code larger than base — capped at FeeBase, zero balance
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $103.50 code capped at $100 FeeBase → zero balance, no payment row")]
    public async Task Absolute_ExactMatch_ZeroBalance()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        var ai = AddDiscountCode(ctx, codeAmount: 103.50m, codeName: "EXACT");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 100m, feeProcessing: 3.50m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("EXACT", (playerId, 103.50m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(100m);

        // Fixed $103.50 capped at FeeBase $100. ReduceProcessingFee: 100 * 0.035 = 3.50 → proc 0.
        // FeeMath(base 100, proc 0, disc 100): total = 100 + 0 - 100 = 0 → zero balance, honestly.
        // The discount is a fee modifier: PaidTotal stays 0 and NO payment row is written.
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(100m);
        dbReg.FeeProcessing.Should().Be(0m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(0m);
        dbReg.DiscountCodeId.Should().Be(ai);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Fixed code far exceeds fee — still capped at FeeBase
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $200 code capped at $100 FeeBase → zero balance, no payment row")]
    public async Task Absolute_ExceedsFee_ClampsToFeeTotal()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        var ai = AddDiscountCode(ctx, codeAmount: 200m, codeName: "BIG");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 100m, feeProcessing: 3.50m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("BIG", (playerId, 103.50m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(100m);

        // Fixed $200 capped at FeeBase $100 (can't discount more than the base fee).
        // ReduceProcessingFee: 100 * 0.035 = 3.50 → proc 0. FeeMath: 100 + 0 - 100 = 0.
        // The discount is a fee modifier: PaidTotal stays 0 and NO payment row is written.
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(100m);
        dbReg.FeeProcessing.Should().Be(0m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(0m);
        dbReg.DiscountCodeId.Should().Be(ai);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5. Percent 100% discount — full waiver, proc zeroed (not re-inflated)
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Percent: 100% off $595 base → full waiver (proc zeroed), no payment row")]
    public async Task Percent_FullDiscount_100Pct()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        var ai = AddDiscountCode(ctx, codeAmount: 100m, bAsPercent: true, codeName: "FREE");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 595m, feeProcessing: 20.83m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("FREE", (playerId, 615.83m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(595m);

        // 100% of FeeBase 595 = 595. ReduceProcessingFee: 595 * 0.035 = 20.83 → proc 0.
        // FeeMath(base 595, proc 0, disc 595): total = 595 + 0 - 595 = 0 → free (no stranded proc).
        // The discount is a fee modifier: PaidTotal stays 0 and NO payment row is written. (A 100% DC
        // used to stamp a fake $595 Correction payment + bump PaidTotal, double-booking the discount.)
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(595m);
        dbReg.FeeProcessing.Should().Be(0m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(0m);
        dbReg.DiscountCodeId.Should().Be(ai);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 6. Percent 50% discount — off FeeBase (not the proc-inclusive amount)
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Percent: 50% off $200 base → discount=$100, proc 3.50, total=$103.50")]
    public async Task Percent_PartialDiscount_50Pct()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 50m, bAsPercent: true, codeName: "HALF");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 200m, feeProcessing: 7.00m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("HALF", (playerId, 207.00m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(100m);

        // 50% of FeeBase 200 = 100 (NOT 50% of the proc-inclusive 207). ReduceProcessingFee:
        // 100 * 0.035 = 3.50, proc 7.00 → 3.50. FeeMath(base 200, proc 3.50, disc 100): total = 103.50.
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(100m);
        dbReg.FeeProcessing.Should().Be(3.50m);
        dbReg.FeeTotal.Should().Be(103.50m);
        dbReg.OwedTotal.Should().Be(103.50m);
        dbReg.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 7. Two players — per-player application off each reg's FeeBase
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $100 across 2 players (base $400/$200) → per-player application with proc reduction")]
    public async Task Absolute_TwoPlayers_PerPlayerApplication()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var player1Id = Guid.NewGuid().ToString();
        var player2Id = Guid.NewGuid().ToString();
        var ai = AddDiscountCode(ctx, codeAmount: 100m, codeName: "SPLIT");
        var reg1 = AddRegistration(ctx, userId: player1Id, feeBase: 400m, feeProcessing: 14.00m, insuredName: "Player One");
        var reg2 = AddRegistration(ctx, userId: player2Id, feeBase: 200m, feeProcessing: 7.00m, insuredName: "Player Two");
        await ctx.SaveChangesAsync();

        // Item amounts (base+proc) are ignored by the server — discount is computed off each reg's FeeBase.
        var request = MakeRequest("SPLIT", (player1Id, 414m), (player2Id, 207m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(200m);
        dto.SuccessCount.Should().Be(2);

        var dbReg1 = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg1.RegistrationId);
        var dbReg2 = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg2.RegistrationId);

        // Per-player: each player gets the full $100 (each base > 100, so uncapped at $100).
        dbReg1.FeeDiscount.Should().Be(100m);
        dbReg2.FeeDiscount.Should().Be(100m);
        dbReg1.DiscountCodeId.Should().Be(ai);
        dbReg2.DiscountCodeId.Should().Be(ai);

        // ReduceProcessingFee for p1: 100 * 0.035 = 3.50, proc 14.00 → 10.50
        // FeeMath(base 400, proc 10.50, disc 100): total = 400+10.50-100 = 310.50
        dbReg1.FeeProcessing.Should().Be(10.50m);
        dbReg1.FeeTotal.Should().Be(310.50m);

        // ReduceProcessingFee for p2: 100 * 0.035 = 3.50, proc 7.00 → 3.50
        // FeeMath(base 200, proc 3.50, disc 100): total = 200+3.50-100 = 103.50
        dbReg2.FeeProcessing.Should().Be(3.50m);
        dbReg2.FeeTotal.Should().Be(103.50m);

        dbReg1.PaidTotal.Should().Be(0m);
        dbReg2.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 8. Rejected — already discounted
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Rejected: player already has discount → Success=false per player, reg unchanged")]
    public async Task Rejected_AlreadyDiscounted()
    {
        var (controller, ctx, _) = await CreateControllerAsync(bAddProcessingFees: false);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 50m, codeName: "DUP");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 200m, feeDiscount: 25m);
        await ctx.SaveChangesAsync();

        var originalFeeTotal = reg.FeeTotal;
        var originalOwed = reg.OwedTotal;

        var request = MakeRequest("DUP", (playerId, 175m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeFalse();
        dto.SuccessCount.Should().Be(0);
        dto.FailureCount.Should().Be(1);
        dto.Results[0].Success.Should().BeFalse();
        dto.Results[0].Message.Should().Contain("already applied");

        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(25m);
        dbReg.FeeTotal.Should().Be(originalFeeTotal);
        dbReg.OwedTotal.Should().Be(originalOwed);
        // Rejected (already discounted) → the code is NOT stamped, so usage isn't over-counted.
        dbReg.DiscountCodeId.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 9. Rejected — expired code
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Rejected: expired discount code → Success=false, 'Invalid or expired'")]
    public async Task Rejected_ExpiredCode()
    {
        var (controller, ctx, _) = await CreateControllerAsync(bAddProcessingFees: false);
        var playerId = Guid.NewGuid().ToString();

        var ai = Interlocked.Increment(ref _discountCodeAiCounter);
        ctx.JobDiscountCodes.Add(new JobDiscountCodes
        {
            Ai = ai,
            JobId = JobId,
            CodeName = "EXPIRED",
            BAsPercent = false,
            CodeAmount = 50m,
            Active = true,
            CodeStartDate = DateTime.UtcNow.AddDays(-30),
            CodeEndDate = DateTime.UtcNow.AddDays(-1),
            LebUserId = "seed",
            Modified = DateTime.UtcNow
        });
        AddRegistration(ctx, userId: playerId, feeBase: 200m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("EXPIRED", (playerId, 200m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeFalse();
        dto.Message.Should().Be("Invalid or expired discount code");
        dto.TotalDiscount.Should().Be(0m);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 10. Pathological — zero amount filtered out before the loop (SP-018)
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Pathological: $100 code with Amount=0 item → filtered out, 'No valid players'")]
    public async Task Pathological_ZeroFeeBase()
    {
        var (controller, ctx, _) = await CreateControllerAsync(bAddProcessingFees: false);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 100m, codeName: "ZERO");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 0m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("ZERO", (playerId, 0m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeFalse();
        dto.Message.Should().Be("No valid players for discount");
        dto.TotalDiscount.Should().Be(0m);

        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(0m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 11. Regression — percent off FeeBase, NOT the client-submitted proc-inclusive amount
    //     The original bug: 50% of $615.83 owed (= $307.92) instead of 50% of $595 base.
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Regression: 50% off $595 base ignores client's $615.83 amount → discount=$297.50, total=$307.92")]
    public async Task Percent_UsesFeeBase_NotClientAmount()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 50m, bAsPercent: true, codeName: "HALF595");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 595m, feeProcessing: 20.83m);
        await ctx.SaveChangesAsync();

        // Client sends the proc-inclusive owed balance (615.83). The server must IGNORE it and
        // compute the discount off FeeBase (595). The old bug computed 50% of 615.83 = 307.92.
        var request = MakeRequest("HALF595", (playerId, 615.83m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(297.50m);

        // 50% of FeeBase 595 = 297.50. ReduceProcessingFee: 297.50 * 0.035 = 10.41, proc 20.83 → 10.42.
        // FeeMath(base 595, proc 10.42, disc 297.50): total = 595 + 10.42 - 297.50 = 307.92.
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(297.50m);
        dbReg.FeeProcessing.Should().Be(10.42m);
        dbReg.FeeTotal.Should().Be(307.92m);
        dbReg.OwedTotal.Should().Be(307.92m);
        dbReg.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 12. Pathological — positive amount but $0 FeeBase → computes to 0, no discount
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Pathological: positive amount but $0 FeeBase → d=0, 'No discount applicable'")]
    public async Task Pathological_ZeroFeeBase_PositiveAmount()
    {
        var (controller, ctx, _) = await CreateControllerAsync(bAddProcessingFees: false);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 50m, bAsPercent: true, codeName: "ZB2");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 0m);
        await ctx.SaveChangesAsync();

        // Amount > 0 passes the selection filter, but FeeBase is 0 so the discount computes to 0.
        var request = MakeRequest("ZB2", (playerId, 100m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeFalse();
        dto.SuccessCount.Should().Be(0);
        dto.Results[0].Success.Should().BeFalse();
        dto.Results[0].Message.Should().Contain("No discount applicable");

        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(0m);
        // No discount applied → the code is NOT stamped, so usage isn't over-counted.
        dbReg.DiscountCodeId.Should().BeNull();
    }
}
