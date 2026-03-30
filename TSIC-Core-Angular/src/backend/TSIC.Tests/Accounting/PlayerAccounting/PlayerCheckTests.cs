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
/// Tests for recording check and correction payments against a single player registration.
///
/// These test RegistrationSearchService.RecordCheckOrCorrectionAsync which:
/// 1. Creates a RegistrationAccounting record
/// 2. Updates reg.PaidTotal
/// 3. Reduces processing fees proportionally (non-CC payments)
/// 4. Recalculates reg.FeeTotal and reg.OwedTotal
///
/// Naming: {Method}_{Scenario}_{Expected}
/// </summary>
public class PlayerCheckTests
{
    private const string UserId = "test-admin";

    /// <summary>
    /// Build a RegistrationSearchService wired to a real InMemory DB.
    /// Real repos for data access, mocked external services (ADN, email, etc).
    /// Returns the job ID so tests can pass it to service methods.
    /// </summary>
    private static async Task<(RegistrationSearchService svc, AccountingDataBuilder builder,
        TSIC.Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId)>
        CreateServiceAsync(decimal processingFeePercent = 3.5m, bool bAddProcessingFees = true)
    {
        var ctx = DbContextFactory.Create();
        var builder = new AccountingDataBuilder(ctx);

        // Seed job first so repos can find it
        var job = builder.AddJob(
            processingFeePercent: processingFeePercent,
            bAddProcessingFees: bAddProcessingFees);
        await builder.SaveAsync();

        var registrationRepo = new RegistrationRepository(ctx);
        var accountingRepo = new RegistrationAccountingRepository(ctx);

        // Mock external services
        var jobRepo = new Mock<IJobRepository>();
        var adnApi = new Mock<IAdnApiService>();
        var textSub = new Mock<ITextSubstitutionService>();
        var emailService = new Mock<IEmailService>();
        var deviceRepo = new Mock<IDeviceRepository>();
        var logger = new Mock<ILogger<RegistrationSearchService>>();

        // Fee resolution
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

    // ─── Test 1: Basic check payment ──────────────────────────────────

    [Fact]
    public async Task RecordCheck_BasicPayment_UpdatesPaidAndOwed()
    {
        // Arrange — player owes $100, no processing fees
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
        updated!.PaidTotal.Should().Be(100m);
        updated.OwedTotal.Should().Be(0m);
    }

    // ─── Test 2: Check with processing fee reduction ──────────────────

    [Fact]
    public async Task RecordCheck_WithProcessingFees_ReducesFeeProportionally()
    {
        // Arrange — player owes $103.50 ($100 base + $3.50 processing at 3.5%)
        var (svc, b, ctx, jobId) = await CreateServiceAsync(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        // Act — record a check for $100 (the base amount)
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
        updated!.PaidTotal.Should().Be(100m);

        // Processing fee reduced by 100 × 0.035 = $3.50
        updated.FeeProcessing.Should().Be(0m, "full processing fee removed when check covers base");

        // FeeTotal recalculated: 100 + 0 - 0 = 100
        updated.FeeTotal.Should().Be(100m);

        // OwedTotal: 100 - 100 = 0
        updated.OwedTotal.Should().Be(0m);
    }

    // ─── Test 3: Partial check payment ────────────────────────────────

    [Fact]
    public async Task RecordCheck_PartialPayment_ReducesFeePartially()
    {
        // Arrange — player owes $103.50 ($100 base + $3.50 processing)
        var (svc, b, ctx, jobId) = await CreateServiceAsync(processingFeePercent: 3.5m, bAddProcessingFees: true);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 3.50m);
        await b.SaveAsync();

        // Act — pay $50 by check (partial)
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
        updated!.PaidTotal.Should().Be(50m);

        // Processing fee reduced by 50 × 0.035 = $1.75
        updated.FeeProcessing.Should().Be(1.75m);

        // FeeTotal: 100 + 1.75 - 0 = 101.75
        updated.FeeTotal.Should().Be(101.75m);

        // OwedTotal: 101.75 - 50 = 51.75
        updated.OwedTotal.Should().Be(51.75m);
    }

    // ─── Test 4: Correction (positive) ────────────────────────────────

    [Fact]
    public async Task RecordCorrection_PositiveAmount_UpdatesPaidTotal()
    {
        // Arrange — player owes $100, no processing fees
        var (svc, b, ctx, jobId) = await CreateServiceAsync(bAddProcessingFees: false);
        var reg = b.AddPlayerRegistration(jobId, feeBase: 100m, feeProcessing: 0m);
        await b.SaveAsync();

        // Act — correction of +$50
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
        updated.OwedTotal.Should().Be(50m);
    }

    // ─── Test 5: Zero check rejected ──────────────────────────────────

    [Fact]
    public async Task RecordCheck_ZeroAmount_ReturnsError()
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

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("$0.00");
    }

    // ─── Test 6: Zero correction rejected ─────────────────────────────

    [Fact]
    public async Task RecordCorrection_ZeroAmount_ReturnsError()
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

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("$0.00");
    }
}
