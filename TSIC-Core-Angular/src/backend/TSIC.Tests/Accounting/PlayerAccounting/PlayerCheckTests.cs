using FluentAssertions;
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
/// Key accounting principle tested:
///   When paying by check (not credit card), the CC processing fee is removed
///   proportionally — because the player isn't using a credit card, they shouldn't
///   pay the CC surcharge.
///
/// Formula: fee reduction = check amount × processing rate (e.g., 3.5%)
/// </summary>
public class PlayerCheckTests
{
    private const string UserId = "test-admin";

    /// <summary>
    /// Builds the service with a real in-memory database.
    /// External services (Authorize.Net, email) are mocked since we're not testing those.
    /// </summary>
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
    //  CHECK PAYMENTS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Player owes $100 (no processing fees). Director records a $100 check.
    /// EXPECTED: Player is fully paid. Balance = $0.
    /// </summary>
    [Fact(DisplayName = "Check: $100 payment against $100 owed → balance $0")]
    public async Task Check_FullPayment_NoProcessingFees_BalanceZero()
    {
        // Arrange — player owes $100, job does NOT charge processing fees
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        // Act — record a $100 check
        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 100m,
                PaymentType = "Check",
                CheckNo = "1234"
            });

        // Assert
        result.Success.Should().BeTrue();
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.PaidTotal.Should().Be(100m, "the full $100 check was applied");
        updated.OwedTotal.Should().Be(0m, "player is fully paid");
    }

    /// <summary>
    /// SCENARIO: Player owes $103.50 ($100 base + $3.50 CC processing fee at 3.5%).
    ///           Director records a $100 check.
    /// EXPECTED: The $3.50 processing fee is removed (check doesn't incur CC fees).
    ///           New total = $100. Paid = $100. Balance = $0.
    /// </summary>
    [Fact(DisplayName = "Check: $100 payment removes $3.50 processing fee → balance $0")]
    public async Task Check_FullBasePayment_RemovesProcessingFee_BalanceZero()
    {
        // Arrange — player owes $103.50 ($100 base + $3.50 processing)
        var (svc, b, ctx, jobId) = await CreateServiceAsync(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        // Act — check for $100 (the base fee, not the inflated $103.50)
        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 100m,
                PaymentType = "Check"
            });

        // Assert
        result.Success.Should().BeTrue();
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);

        updated!.FeeProcessing.Should().Be(0m,
            "processing fee removed: $100 × 3.5% = $3.50 reduction, was $3.50 → now $0");
        updated.FeeTotal.Should().Be(100m,
            "fee total recalculated: $100 base + $0 processing = $100");
        updated.PaidTotal.Should().Be(100m);
        updated.OwedTotal.Should().Be(0m, "player fully paid after fee adjustment");
    }

    /// <summary>
    /// SCENARIO: Player owes $103.50 ($100 base + $3.50 processing).
    ///           Director records a $50 partial check.
    /// EXPECTED: Processing fee partially reduced by $50 × 3.5% = $1.75.
    ///           Remaining processing fee = $1.75.
    ///           New total = $101.75. Paid = $50. Balance = $51.75.
    /// </summary>
    [Fact(DisplayName = "Check: $50 partial payment reduces processing fee by $1.75 → balance $51.75")]
    public async Task Check_PartialPayment_ReducesProcessingFeeProportionally()
    {
        // Arrange
        var (svc, b, ctx, jobId) = await CreateServiceAsync(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        // Act — $50 partial check
        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 50m,
                PaymentType = "Check"
            });

        // Assert
        result.Success.Should().BeTrue();
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);

        updated!.FeeProcessing.Should().Be(1.75m,
            "processing fee reduced by $50 × 3.5% = $1.75; was $3.50 → now $1.75");
        updated.FeeTotal.Should().Be(101.75m,
            "fee total: $100 base + $1.75 processing = $101.75");
        updated.PaidTotal.Should().Be(50m);
        updated.OwedTotal.Should().Be(51.75m,
            "balance: $101.75 total − $50 paid = $51.75");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CORRECTIONS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Player owes $100. Director records a +$50 correction (scholarship).
    /// EXPECTED: Paid = $50. Balance = $50. (Corrections also reduce processing fees
    ///           but this job has none configured.)
    /// </summary>
    [Fact(DisplayName = "Correction: +$50 scholarship against $100 owed → balance $50")]
    public async Task Correction_PositiveAmount_ReducesBalance()
    {
        // Arrange — no processing fees
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        // Act
        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 50m,
                PaymentType = "Correction",
                Comment = "Scholarship"
            });

        // Assert
        result.Success.Should().BeTrue();
        var updated = await ctx.Registrations.FindAsync(reg.RegistrationId);
        updated!.PaidTotal.Should().Be(50m);
        updated.OwedTotal.Should().Be(50m, "balance reduced by correction amount");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VALIDATION — Bad inputs should be rejected
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// SCENARIO: Director tries to record a $0 check.
    /// EXPECTED: Rejected — checks must be greater than $0.
    /// </summary>
    [Fact(DisplayName = "Validation: $0 check is rejected")]
    public async Task Check_ZeroAmount_Rejected()
    {
        var (svc, b, _, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 0m,
                PaymentType = "Check"
            });

        result.Success.Should().BeFalse("a $0 check should be rejected");
        result.Error.Should().Contain("$0.00");
    }

    /// <summary>
    /// SCENARIO: Director tries to record a $0 correction.
    /// EXPECTED: Rejected — corrections cannot be $0.
    /// </summary>
    [Fact(DisplayName = "Validation: $0 correction is rejected")]
    public async Task Correction_ZeroAmount_Rejected()
    {
        var (svc, b, _, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId);
        await b.SaveAsync();

        var result = await svc.RecordCheckOrCorrectionAsync(jobId, UserId,
            new RegistrationCheckOrCorrectionRequest
            {
                RegistrationId = reg.RegistrationId,
                Amount = 0m,
                PaymentType = "Correction"
            });

        result.Success.Should().BeFalse("a $0 correction should be rejected");
        result.Error.Should().Contain("$0.00");
    }
}
