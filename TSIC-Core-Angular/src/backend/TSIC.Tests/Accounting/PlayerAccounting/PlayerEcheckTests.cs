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
/// PLAYER ECHECK TESTS
///
/// These tests validate what happens when a director records an eCheck payment
/// against a single player registration (search/registrations).
///
/// Key accounting principle (mirror of mail-in check, but PARTIAL credit):
///   When paying by eCheck, the customer still incurs the eCheck processing rate.
///   So the CC processing fee is reduced by ONLY the difference between rates:
///
///     fee reduction = echeck amount × (CC_rate − EC_rate)
///
///   At default rates (CC=3.5%, EC=1.5%) → reduction rate = 2.0%.
///   The remaining 1.5% stays on the registration as the eCheck processing cost.
///
/// Each test verifies BOTH:
///   1. The accounting record created (PaymentMethodId = EcheckMethodId, amounts, check #)
///   2. The registration financial state after (FeeProcessing, FeeTotal, PaidTotal, OwedTotal)
/// </summary>
public class PlayerEcheckTests
{
    private const string UserId = "test-admin";

    private static async Task<(RegistrationSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId)>
        CreateServiceAsync(
            decimal processingFeePercent = 3.5m,
            decimal ecprocessingFeePercent = 1.5m,
            bool bAddProcessingFees = true)
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
        feeService.Setup(f => f.GetEffectiveEcheckProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ecprocessingFeePercent / 100m);

        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = bAddProcessingFees,
                BApplyProcessingFeesToTeamDeposit = false,
                BTeamsFullPaymentRequired = false,
                PaymentMethodsAllowedCode = 7
            });

        var feeAdjustment = new RegistrationFeeAdjustmentService(jobRepo.Object, feeService.Object);

        var arbRepo = new Mock<IArbSubscriptionRepository>();
        var familiesRepo = new Mock<IFamiliesRepository>();
        var svc = new RegistrationSearchService(
            registrationRepo, accountingRepo, jobRepo.Object, familiesRepo.Object, deviceRepo.Object,
            adnApi.Object, arbRepo.Object, textSub.Object, emailService.Object, feeAdjustment, logger.Object);

        return (svc, builder, ctx, job.JobId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ECHECK PAYMENTS (PaymentMethod = "E-Check Payment")
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Player owes $100 (no processing fees). Director records a $100 eCheck.
    /// RECORD CREATED: ECheck payment, Payamt=$100, PaymentMethodId=Echeck
    /// REGISTRATION AFTER: PaidTotal=$100, OwedTotal=$0, FeeProcessing unchanged ($0)
    /// </summary>
    [Fact(DisplayName = "ECheck: $100 paid by eCheck → ECheck record created, balance $0")]
    public async Task Echeck_FullPayment_CreatesEcheckRecord_BalanceZero()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 100m,
                PaymentType = "ECheck",
                CheckNo = "EC-1234"
            });

        // ── Verify result ──
        result.Success.Should().BeTrue();

        // ── Verify accounting record ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record.Should().NotBeNull("an ECheck accounting record should be created");
        record!.RegistrationId.Should().Be(reg.RegistrationId);
        record.PaymentMethodId.Should().Be(AccountingDataBuilder.EcheckMethodId,
            "payment method should be 'E-Check Payment'");
        record.Payamt.Should().Be(100m, "paid amount = $100");
        record.CheckNo.Should().Be("EC-1234", "eCheck reference number should be stored");
        record.Active.Should().BeTrue();

        // ── Verify registration state ──
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.PaidTotal.Should().Be(100m);
        updated.OwedTotal.Should().Be(0m, "fully paid");
        updated.FeeProcessing.Should().Be(0m, "no processing fees to begin with");
    }

    /// <summary>
    /// SCENARIO: Player owes $103.50 ($100 base + $3.50 CC processing at 3.5%).
    ///           Director records a $100 eCheck (CC=3.5%, EC=1.5%, diff=2.0%).
    /// RECORD CREATED: ECheck payment, Payamt=$100
    /// FEE IMPACT: FeeProcessing reduced by $100 × 2.0% = $2.00 (from $3.50 → $1.50)
    /// REGISTRATION AFTER: FeeTotal=$101.50, PaidTotal=$100, OwedTotal=$1.50
    /// WHY: Customer pays via eCheck (still incurs 1.5% rate), so refund only the diff
    ///       between CC rate (baked into FeeProcessing) and the EC rate they're now paying.
    /// </summary>
    [Fact(DisplayName = "ECheck: $100 eCheck removes $2.00 fee → FeeProcessing=$1.50, balance $1.50")]
    public async Task Echeck_FullBase_RemovesFeeByDiff_RetainsEcRate()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, ecprocessingFeePercent: 1.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 100m,
                PaymentType = "ECheck"
            });

        result.Success.Should().BeTrue();

        // ── Verify accounting record ──
        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.EcheckMethodId);
        record.Payamt.Should().Be(100m, "eCheck amount recorded as-is");

        // ── Verify partial fee adjustment (CC − EC diff, not full CC) ──
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.FeeProcessing.Should().Be(1.50m,
            "partial reduction: $100 × (3.5% − 1.5%) = $2.00; was $3.50 → $1.50");
        updated.FeeTotal.Should().Be(101.50m,
            "recalculated: $100 base + $1.50 processing = $101.50");
        updated.PaidTotal.Should().Be(100m);
        updated.OwedTotal.Should().Be(1.50m,
            "still owes the eCheck processing rate ($100 × 1.5% = $1.50)");
    }

    /// <summary>
    /// SCENARIO: Player owes $103.50. Director records a $50 partial eCheck.
    /// RECORD CREATED: ECheck payment, Payamt=$50
    /// FEE IMPACT: FeeProcessing reduced by $50 × 2.0% = $1.00 (from $3.50 → $2.50)
    /// REGISTRATION AFTER: FeeTotal=$102.50, PaidTotal=$50, OwedTotal=$52.50
    /// </summary>
    [Fact(DisplayName = "ECheck: $50 partial reduces FeeProcessing by $1.00 → balance $52.50")]
    public async Task Echeck_Partial_ReducesFeeProportionallyByDiff()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(
            processingFeePercent: 3.5m, ecprocessingFeePercent: 1.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 50m,
                PaymentType = "ECheck"
            });

        result.Success.Should().BeTrue();

        var record = await ctx.RegistrationAccounting
            .FirstOrDefaultAsync(r => r.RegistrationId == reg.RegistrationId);
        record!.PaymentMethodId.Should().Be(AccountingDataBuilder.EcheckMethodId);
        record.Payamt.Should().Be(50m);

        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.FeeProcessing.Should().Be(2.50m,
            "reduced by $50 × (3.5% − 1.5%) = $1.00; was $3.50 → $2.50");
        updated.FeeTotal.Should().Be(102.50m,
            "$100 base + $2.50 processing = $102.50");
        updated.PaidTotal.Should().Be(50m);
        updated.OwedTotal.Should().Be(52.50m,
            "$102.50 − $50 = $52.50");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VALIDATION — Bad inputs should be rejected, no records created
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Director tries to record a $0 eCheck.
    /// EXPECTED: Rejected. No accounting record created.
    /// </summary>
    [Fact(DisplayName = "Validation: $0 eCheck rejected — no record created")]
    public async Task Echeck_ZeroAmount_Rejected_NoRecord()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 0m,
                PaymentType = "ECheck"
            });

        result.Success.Should().BeFalse("$0 eCheck should be rejected");
        result.Error.Should().Contain("$0.00");

        var recordCount = await ctx.RegistrationAccounting
            .CountAsync(r => r.RegistrationId == reg.RegistrationId);
        recordCount.Should().Be(0, "no accounting record should be created for rejected payment");
    }

    /// <summary>
    /// SCENARIO: Player owes $100. Director tries to record a $150 eCheck.
    /// EXPECTED: Rejected with clear error. No accounting record created.
    /// </summary>
    [Fact(DisplayName = "Validation: eCheck exceeding balance rejected — no record created")]
    public async Task Echeck_ExceedsBalance_Rejected_NoRecord()
    {
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 150m,
                PaymentType = "ECheck",
                CheckNo = "EC-9999"
            });

        result.Success.Should().BeFalse("eCheck exceeding balance should be rejected");
        result.Error.Should().Contain("exceeds");
        result.Error.Should().Contain("$100.00");

        var recordCount = await ctx.RegistrationAccounting
            .CountAsync(r => r.RegistrationId == reg.RegistrationId);
        recordCount.Should().Be(0, "no accounting record should be created for overpayment");
    }
}
