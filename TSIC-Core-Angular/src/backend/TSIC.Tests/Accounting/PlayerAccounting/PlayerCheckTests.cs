using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Accounting.PlayerAccounting;

/// <summary>
/// PLAYER CHECK &amp; CORRECTION TESTS
///
/// These tests validate what happens when a director records a check payment
/// or a manual correction against a single player registration (search/registrations).
///
/// Key accounting principle:
///   When paying by check (not CC), the CC processing fee is removed proportionally —
///   because the player isn't using a credit card, they shouldn't pay the CC surcharge.
///
///   Formula: fee reduction = check amount × processing rate (e.g., 3.5%)
///
/// Each test verifies BOTH:
///   1. The accounting record created (payment method, amounts, check #)
///   2. The registration financial state after (FeeProcessing, FeeTotal, PaidTotal, OwedTotal)
/// </summary>
public class PlayerCheckTests
{
    private const string UserId = "test-admin";

    private static async Task<(RegistrationSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId)>
        CreateServiceAsync(decimal processingFeePercent = 3.5m, bool bAddProcessingFees = true)
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        var job = builder.AddJob(
            processingFeePercent: processingFeePercent,
            bAddProcessingFees: bAddProcessingFees);
        await builder.SaveAsync();

        var registrationRepo = new RegistrationRepository(ctx);
        var accountingRepo = new RegistrationAccountingRepository(ctx);

        var jobRepo = new Mock<IJobRepository>();
        var adnApi = new Mock<IAdnApiService>();
        var textSub = new Mock<ITextSubstitutionService>();
        var emailService = new Mock<IEmailService>();
        var deviceRepo = new Mock<IDeviceRepository>();
        var logger = new Mock<ILogger<RegistrationSearchService>>();

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingFeePercent / 100m);

        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = bAddProcessingFees,
                BApplyProcessingFeesToTeamDeposit = false,
                BTeamsFullPaymentRequired = false,
                PaymentMethodsAllowedCode = 7
            });

        var feeAdjustment = new RegistrationFeeAdjustmentService(jobRepo.Object, feeService.Object);

        var svc = new RegistrationSearchService(
            registrationRepo, accountingRepo, jobRepo.Object, deviceRepo.Object,
            adnApi.Object, textSub.Object, emailService.Object, feeAdjustment, logger.Object);

        return (svc, builder, ctx, job.JobId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CHECK PAYMENTS (PaymentMethod = "Check Payment By Client")
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Player owes $100 (no processing fees). Director records a $100 check.
    /// RECORD CREATED: Check payment, Payamt=$100, PaymentMethodId=Check
    /// REGISTRATION AFTER: PaidTotal=$100, OwedTotal=$0, FeeProcessing unchanged ($0)
    /// </summary>
    [Fact(DisplayName = "Check: $100 paid by check → Check record created, balance $0")]
    public async Task Check_FullPayment_CreatesCheckRecord_BalanceZero()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 100m,
                PaymentType = "Check",
                CheckNo = "1234"
            });

        // ── Verify result ──
        result.Success.Should().BeTrue();

        // ── Verify accounting record ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record.Should().NotBeNull("a Check accounting record should be created");
        record!.RegistrationId.Should().Be(reg.RegistrationId,
            "accounting record should be linked to the player's registration");
        record.PaymentMethodId.Should().Be(AccountingDataBuilder.CheckMethodId,
            "payment method should be 'Check Payment By Client'");
        record.Payamt.Should().Be(100m, "paid amount = $100");
        record.CheckNo.Should().Be("1234", "check number should be stored");
        record.Active.Should().BeTrue();

        // ── Verify registration state ──
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.PaidTotal.Should().Be(100m);
        updated.OwedTotal.Should().Be(0m, "fully paid");
        updated.FeeProcessing.Should().Be(0m, "no processing fees to begin with");
    }

    /// <summary>
    /// SCENARIO: Player owes $103.50 ($100 base + $3.50 CC processing at 3.5%).
    ///           Director records a $100 check.
    /// RECORD CREATED: Check payment, Payamt=$100
    /// FEE IMPACT: FeeProcessing reduced from $3.50 → $0 (100 × 3.5% = $3.50 reduction)
    /// REGISTRATION AFTER: FeeTotal=$100, PaidTotal=$100, OwedTotal=$0
    /// WHY: Paying by check means no CC processing fee — the $3.50 surcharge is removed.
    /// </summary>
    [Fact(DisplayName = "Check: $100 check removes $3.50 processing fee → FeeProcessing=$0, balance $0")]
    public async Task Check_FullBase_RemovesProcessingFee_BalanceZero()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 100m,
                PaymentType = "Check"
            });

        result.Success.Should().BeTrue();

        // ── Verify accounting record ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.CheckMethodId);
        record.Payamt.Should().Be(100m, "check amount recorded as-is");

        // ── Verify fee adjustment ──
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.FeeProcessing.Should().Be(0m,
            "processing fee removed: $100 × 3.5% = $3.50, was $3.50 → $0");
        updated.FeeTotal.Should().Be(100m,
            "recalculated: $100 base + $0 processing = $100");
        updated.PaidTotal.Should().Be(100m);
        updated.OwedTotal.Should().Be(0m, "fully paid after fee removal");
    }

    /// <summary>
    /// SCENARIO: Player owes $103.50. Director records a $50 partial check.
    /// RECORD CREATED: Check payment, Payamt=$50
    /// FEE IMPACT: FeeProcessing reduced by $50 × 3.5% = $1.75 (from $3.50 → $1.75)
    /// REGISTRATION AFTER: FeeTotal=$101.75, PaidTotal=$50, OwedTotal=$51.75
    /// WHY: Only the portion paid by check gets the fee reduction.
    /// </summary>
    [Fact(DisplayName = "Check: $50 partial reduces FeeProcessing by $1.75 → balance $51.75")]
    public async Task Check_Partial_ReducesFeeProportionally()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 50m,
                PaymentType = "Check"
            });

        result.Success.Should().BeTrue();

        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.CheckMethodId);
        record.Payamt.Should().Be(50m);

        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.FeeProcessing.Should().Be(1.75m,
            "reduced by $50 × 3.5% = $1.75; was $3.50 → $1.75");
        updated.FeeTotal.Should().Be(101.75m,
            "$100 base + $1.75 processing = $101.75");
        updated.PaidTotal.Should().Be(50m);
        updated.OwedTotal.Should().Be(51.75m,
            "$101.75 − $50 = $51.75");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CORRECTIONS (PaymentMethod = "Correction")
    //  NOT from a discount code — just a manual adjustment by a director.
    //  Corrections DO reduce processing fees (same principle as checks).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Player owes $100. Director records a +$50 correction (scholarship).
    ///           Job does NOT have processing fees enabled.
    /// RECORD CREATED: Correction, Payamt=$50, PaymentMethodId=Correction, no DiscountCodeId
    /// FEE IMPACT: None (job has no processing fees)
    /// REGISTRATION AFTER: PaidTotal=$50, OwedTotal=$50
    /// NOTE: This is NOT a discount code — discount codes set DiscountCodeAi on the record
    ///       and are applied through the team registration flow, not through search/registrations.
    /// </summary>
    [Fact(DisplayName = "Correction: +$50 manual adjustment → Correction record (no DC), balance $50")]
    public async Task Correction_ManualAdjustment_CreatesCorrectionRecord()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 50m,
                PaymentType = "Correction",
                Comment = "Scholarship"
            });

        result.Success.Should().BeTrue();

        // ── Verify accounting record ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record.Should().NotBeNull();
        record!.RegistrationId.Should().Be(reg.RegistrationId,
            "accounting record should be linked to the player's registration");
        record.PaymentMethodId.Should().Be(AccountingDataBuilder.CorrectionMethodId,
            "payment method should be 'Correction'");
        record.Payamt.Should().Be(50m);
        record.Comment.Should().Be("Scholarship");
        record.DiscountCodeAi.Should().BeNull(
            "this is a manual correction, NOT a discount code — DiscountCodeAi should be null");
        record.Active.Should().BeTrue();

        // ── Verify registration state ──
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.PaidTotal.Should().Be(50m);
        updated.OwedTotal.Should().Be(50m);
    }

    /// <summary>
    /// SCENARIO: Player owes $103.50 ($100 + $3.50 processing).
    ///           Director records +$50 correction with processing fees enabled.
    /// RECORD CREATED: Correction, Payamt=$50
    /// FEE IMPACT: FeeProcessing reduced by $50 × 3.5% = $1.75 (same as check)
    /// REGISTRATION AFTER: FeeProcessing=$1.75, FeeTotal=$101.75, PaidTotal=$50, OwedTotal=$51.75
    /// WHY: Corrections reduce processing fees the same way checks do — the principle is
    ///       "non-CC payments don't incur CC fees."
    /// </summary>
    [Fact(DisplayName = "Correction: +$50 with processing fees → FeeProcessing reduced by $1.75")]
    public async Task Correction_WithProcessingFees_ReducesFeeProportionally()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 50m,
                PaymentType = "Correction",
                Comment = "Partial scholarship"
            });

        result.Success.Should().BeTrue();

        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.CorrectionMethodId);

        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.FeeProcessing.Should().Be(1.75m,
            "correction reduces processing fee same as check: $50 × 3.5% = $1.75");
        updated.FeeTotal.Should().Be(101.75m);
        updated.PaidTotal.Should().Be(50m);
        updated.OwedTotal.Should().Be(51.75m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VALIDATION — Bad inputs should be rejected, no records created
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Director tries to record a $0 check.
    /// EXPECTED: Rejected. No accounting record created.
    /// </summary>
    [Fact(DisplayName = "Validation: $0 check rejected — no record created")]
    public async Task Check_ZeroAmount_Rejected_NoRecord()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 0m,
                PaymentType = "Check"
            });

        result.Success.Should().BeFalse("$0 check should be rejected");
        result.Error.Should().Contain("$0.00");

        var recordCount = await ctx.RegistrationAccounting
            .CountAsync(r => r.RegistrationId == reg.RegistrationId);
        recordCount.Should().Be(0, "no accounting record should be created for rejected payment");
    }

    /// <summary>
    /// SCENARIO: Player owes $100. Director tries to record a $150 check.
    /// EXPECTED: Rejected with clear error. No accounting record created.
    /// </summary>
    [Fact(DisplayName = "Validation: check exceeding balance rejected — no record created")]
    public async Task Check_ExceedsBalance_Rejected_NoRecord()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 150m,
                PaymentType = "Check",
                CheckNo = "9999"
            });

        result.Success.Should().BeFalse("check exceeding balance should be rejected");
        result.Error.Should().Contain("exceeds");
        result.Error.Should().Contain("$100.00");

        var recordCount = await ctx.RegistrationAccounting
            .CountAsync(r => r.RegistrationId == reg.RegistrationId);
        recordCount.Should().Be(0, "no accounting record should be created for overpayment");
    }

    /// <summary>
    /// SCENARIO: Player owes $100. Director tries to record a +$150 correction.
    /// EXPECTED: Rejected with clear error. No accounting record created.
    /// </summary>
    [Fact(DisplayName = "Validation: correction exceeding balance rejected — no record created")]
    public async Task Correction_ExceedsBalance_Rejected_NoRecord()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 150m,
                PaymentType = "Correction",
                Comment = "Too generous"
            });

        result.Success.Should().BeFalse("correction exceeding balance should be rejected");
        result.Error.Should().Contain("exceeds");
        result.Error.Should().Contain("$100.00");

        var recordCount = await ctx.RegistrationAccounting
            .CountAsync(r => r.RegistrationId == reg.RegistrationId);
        recordCount.Should().Be(0, "no accounting record should be created for overpayment");
    }

    /// <summary>
    /// SCENARIO: Director tries to record a $0 correction.
    /// EXPECTED: Rejected. No accounting record created.
    /// </summary>
    [Fact(DisplayName = "Validation: $0 correction rejected — no record created")]
    public async Task Correction_ZeroAmount_Rejected_NoRecord()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 0m,
                PaymentType = "Correction"
            });

        result.Success.Should().BeFalse("$0 correction should be rejected");
        result.Error.Should().Contain("$0.00");

        var recordCount = await ctx.RegistrationAccounting
            .CountAsync(r => r.RegistrationId == reg.RegistrationId);
        recordCount.Should().Be(0, "no record created for rejected correction");
    }
}
