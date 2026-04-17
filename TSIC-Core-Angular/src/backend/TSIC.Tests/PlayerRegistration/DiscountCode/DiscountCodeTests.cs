using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using TSIC.API.Controllers;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Players;
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
/// Covers absolute and percent discounts, proportional multi-player distribution,
/// processing fee reduction, zero-balance correction rows, clamping, and rejection paths.
///
/// Each test uses real repositories against an in-memory database.
/// Only IJobLookupService, IPaymentService, IJobRepository, IFeeResolutionService, and ILogger are mocked.
///
/// IMPORTANT — ComputeTotals behavior:
/// When reg.FeeProcessing is 0, the controller passes null as the processing override,
/// so ComputeTotals falls back to default 3.5% of FeeBase (FeeConstants.MinProcessingFeePercent).
/// Tests that need clean zero-balance scenarios use bAddProcessingFees=true with explicit
/// processing fees on the registration so the override path is taken and ReduceProcessingFee
/// properly adjusts the fee before ComputeTotals.
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
        var accountingRepo = new RegistrationAccountingRepository(ctx);
        var discountCodeRepo = new JobDiscountCodeRepository(ctx);
        var feeCalc = new PlayerFeeCalculator();

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
        var logger = new Mock<ILogger<PlayerRegistrationPaymentController>>();

        var controller = new PlayerRegistrationPaymentController(
            jobLookup.Object,
            paymentService.Object,
            discountCodeRepo,
            accountingRepo,
            registrationRepo,
            feeCalc,
            feeAdjustment,
            logger.Object);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, FamilyUserId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return (controller, ctx, builder);
    }

    private static JobDiscountCodes AddDiscountCode(
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
        return code;
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

    [Fact(DisplayName = "Absolute: $100 off $615.83 (base=$595 + 3.5% proc) → proc reduced, total=$512.33")]
    public async Task Absolute_PartialDiscount_WithProcessingFees()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 100m, codeName: "SAVE100");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 595m, feeProcessing: 20.83m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("SAVE100", (playerId, 615.83m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(100m);
        dto.SuccessCount.Should().Be(1);

        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(100m);
        dbReg.FeeProcessing.Should().Be(17.33m);
        dbReg.FeeTotal.Should().Be(512.33m);
        dbReg.OwedTotal.Should().Be(512.33m);
        dbReg.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Absolute partial discount — no processing fees on job
    //    (ComputeTotals adds default 3.5% processing when reg.FeeProcessing is 0)
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $100 off $595 (no proc on reg) → ComputeTotals adds default proc, total=$515.83")]
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

        // ComputeTotals fallback: 3.5% of 595 = 20.83 processing added
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(100m);
        dbReg.FeeProcessing.Should().Be(20.83m);
        dbReg.FeeTotal.Should().Be(515.83m);
        dbReg.OwedTotal.Should().Be(515.83m);
        dbReg.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. Absolute exact match — zero balance triggers correction row
    //    Uses processing fees so the override path yields clean zero
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $103.50 off $103.50 (base=$100 + $3.50 proc) → zero balance, correction row")]
    public async Task Absolute_ExactMatch_ZeroBalance()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 103.50m, codeName: "EXACT");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 100m, feeProcessing: 3.50m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("EXACT", (playerId, 103.50m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(103.50m);

        // ReduceProcessingFee: 103.50 * 0.035 = 3.62 capped at 3.50 → FeeProcessing becomes 0
        // ComputeTotals(100, 103.50, 0, null) — null because FeeProcessing is now 0
        //   → default proc = 100*0.035 = 3.50, total = max(0, 100+3.50-103.50) = 0
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(103.50m);
        dbReg.FeeProcessing.Should().Be(3.50m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(103.50m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().HaveCount(1);
        acctRows[0].Payamt.Should().Be(103.50m);
        acctRows[0].Dueamt.Should().Be(103.50m);
        acctRows[0].PaymentMethodId.Should().Be(AccountingDataBuilder.CorrectionMethodId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Absolute exceeds fee — clamped to total
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $200 off $103.50 (base=$100 + $3.50 proc) → clamped to $103.50, correction row")]
    public async Task Absolute_ExceedsFee_ClampsToFeeTotal()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 200m, codeName: "BIG");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 100m, feeProcessing: 3.50m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("BIG", (playerId, 103.50m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(103.50m);

        // ReduceProcessingFee: 103.50 * 0.035 = 3.62 capped at 3.50 → FeeProcessing becomes 0
        // ComputeTotals(100, 103.50, 0, null): default proc=3.50, total = max(0, 100+3.50-103.50) = 0
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(103.50m);
        dbReg.FeeProcessing.Should().Be(3.50m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(103.50m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().HaveCount(1);
        acctRows[0].Payamt.Should().Be(103.50m);
        acctRows[0].PaymentMethodId.Should().Be(AccountingDataBuilder.CorrectionMethodId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5. Percent 100% discount — full waiver
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Percent: 100% off $615.83 (base=$595 + $20.83 proc) → full waiver, correction row")]
    public async Task Percent_FullDiscount_100Pct()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var playerId = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 100m, bAsPercent: true, codeName: "FREE");
        var reg = AddRegistration(ctx, userId: playerId, feeBase: 595m, feeProcessing: 20.83m);
        await ctx.SaveChangesAsync();

        var request = MakeRequest("FREE", (playerId, 615.83m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(615.83m);

        // Percent discount: 615.83 * 1.0 = 615.83
        // ReduceProcessingFee: 615.83 * 0.035 = 21.55 capped at 20.83 → FeeProcessing becomes 0
        // ComputeTotals(595, 615.83, 0, null): default proc=20.83, total = max(0, 595+20.83-615.83) = 0
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(615.83m);
        dbReg.FeeProcessing.Should().Be(20.83m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(615.83m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().HaveCount(1);
        acctRows[0].Payamt.Should().Be(615.83m);
        acctRows[0].PaymentMethodId.Should().Be(AccountingDataBuilder.CorrectionMethodId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 6. Percent 50% discount — partial
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Percent: 50% off $207.00 (base=$200 + $7.00 proc) → discount=$103.50, total=$100")]
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
        dto.TotalDiscount.Should().Be(103.50m);

        // Percent discount: 207.00 * 0.50 = 103.50
        // ReduceProcessingFee: 103.50 * 0.035 = 3.62, feeProcessing 7.00 → 3.38
        // ComputeTotals(200, 103.50, 0, override=3.38): total = 200 + 3.38 - 103.50 = 99.88
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeDiscount.Should().Be(103.50m);
        dbReg.FeeProcessing.Should().Be(3.38m);
        dbReg.FeeTotal.Should().Be(99.88m);
        dbReg.OwedTotal.Should().Be(99.88m);
        dbReg.PaidTotal.Should().Be(0m);

        var acctRows = await ctx.RegistrationAccounting.Where(a => a.RegistrationId == reg.RegistrationId).ToListAsync();
        acctRows.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 7. Two players — proportional distribution
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Absolute: $100 across 2 players ($414/$207) → proportional split with proc reduction")]
    public async Task Absolute_TwoPlayers_ProportionalDistribution()
    {
        var (controller, ctx, _) = await CreateControllerAsync(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true);
        var player1Id = Guid.NewGuid().ToString();
        var player2Id = Guid.NewGuid().ToString();
        AddDiscountCode(ctx, codeAmount: 100m, codeName: "SPLIT");
        var reg1 = AddRegistration(ctx, userId: player1Id, feeBase: 400m, feeProcessing: 14.00m, insuredName: "Player One");
        var reg2 = AddRegistration(ctx, userId: player2Id, feeBase: 200m, feeProcessing: 7.00m, insuredName: "Player Two");
        await ctx.SaveChangesAsync();

        // Items amounts are what frontend sends: base+processing
        var request = MakeRequest("SPLIT", (player1Id, 414m), (player2Id, 207m));
        var result = await controller.ApplyDiscount(request);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ApplyDiscountResponseDto>().Subject;
        dto.Success.Should().BeTrue();
        dto.TotalDiscount.Should().Be(100m);
        dto.SuccessCount.Should().Be(2);

        var dbReg1 = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg1.RegistrationId);
        var dbReg2 = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg2.RegistrationId);

        // Proportional: total=621, p1 weight=414/621=0.6667, p2 weight=207/621=0.3333
        // p1 discount = round(100 * 0.6667, 2) = 66.67
        // p2 discount = round(100 * 0.3333, 2) = 33.33
        dbReg1.FeeDiscount.Should().Be(66.67m);
        dbReg2.FeeDiscount.Should().Be(33.33m);
        (dbReg1.FeeDiscount + dbReg2.FeeDiscount).Should().Be(100m);

        // ReduceProcessingFee for p1: 66.67 * 0.035 = 2.33, proc 14.00 → 11.67
        // ComputeTotals(400, 66.67, 0, 11.67): total = 400+11.67-66.67 = 345.00
        dbReg1.FeeProcessing.Should().Be(11.67m);
        dbReg1.FeeTotal.Should().Be(345.00m);

        // ReduceProcessingFee for p2: 33.33 * 0.035 = 1.17, proc 7.00 → 5.83
        // ComputeTotals(200, 33.33, 0, 5.83): total = 200+5.83-33.33 = 172.50
        dbReg2.FeeProcessing.Should().Be(5.83m);
        dbReg2.FeeTotal.Should().Be(172.50m);

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
    // 10. Pathological — zero fee base (SP-018)
    // ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Pathological: $100 code on $0 feeBase → items filtered out (Amount=0), 'No valid players'")]
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
}
