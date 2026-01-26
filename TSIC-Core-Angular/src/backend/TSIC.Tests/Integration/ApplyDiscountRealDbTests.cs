using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TSIC.API.Controllers;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.Jobs;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace TSIC.Tests.Integration;

/// <summary>
/// Real-database integration test that exercises the ApplyDiscount flow end-to-end
/// and verifies that processing fees and owed totals are reduced proportionally.
/// Uses the same jobId audited in FeeProcessingAuditTests and rolls back all changes.
/// </summary>
public class ApplyDiscountRealDbTests
{
    private readonly ITestOutputHelper _output;
    private const string DefaultConnection = "Server=TSIC-SEDONA\\SS2016;Database=TSICV5;Integrated Security=true;TrustServerCertificate=true;";
    private static readonly Guid TargetJobId = Guid.Parse("9225D9C2-E64A-498A-8E6B-02270E8F97EB");
    private const string TestDiscountCode = "INTEG-FEEPROC-100";

    public ApplyDiscountRealDbTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task apply_discount_reduces_processing_fee_and_owed_total()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();

        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobId == TargetJobId);
        if (job == null || string.IsNullOrWhiteSpace(job.JobPath))
        {
            Console.WriteLine("Job not found or missing jobPath");
            return;
        }

        // Pick a player registration without an existing discount and unpaid (PaidTotal = 0)
        // Note: Removed BActive filter to find any unpaid registrations.
        var registration = await db.Registrations
            .Include(r => r.Role)
            .Include(r => r.User)
            .AsTracking()
            .Where(r => r.JobId == TargetJobId
                        && r.Role!.Name == "Player"
                        && r.FeeDiscount == 0m
                        && r.FeeProcessing > 0m
                        && r.PaidTotal == 0m  // Unpaid registration
                        && r.FamilyUserId != null
                        && r.UserId != null)
            .OrderByDescending(r => r.Modified)
            .FirstOrDefaultAsync();

        if (registration == null)
        {
            Console.WriteLine("No eligible registration found (player with no discount, processing fee > 0, and PaidTotal = 0)");
            return;
        }

        var originalFeeProcessing = registration.FeeProcessing;
        var originalFeeTotal = registration.FeeTotal;
        var originalOwed = registration.OwedTotal;
        var originalPaid = registration.PaidTotal;
        var originalDonation = registration.FeeDonation;
        var originalBase = registration.FeeBase;

        // Seed a live discount code scoped to this job
        var discountCode = new JobDiscountCodes
        {
            JobId = TargetJobId,
            CodeName = TestDiscountCode,
            BAsPercent = false,
            CodeAmount = 100m,
            Active = true,
            CodeStartDate = DateTime.UtcNow.AddDays(-1),
            CodeEndDate = DateTime.UtcNow.AddDays(1),
            LebUserId = registration.FamilyUserId!,
            Modified = DateTime.UtcNow
        };
        db.JobDiscountCodes.Add(discountCode);
        await db.SaveChangesAsync();

        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var percent = await jobRepo.GetProcessingFeePercentAsync(TargetJobId) ?? 3.5m;

        var controller = BuildController(scope, job.JobPath, registration.FamilyUserId!);

        var request = new ApplyDiscountRequestDto
        {
            JobPath = job.JobPath,
            Code = TestDiscountCode,
            Items =
            {
                new ApplyDiscountItemDto
                {
                    PlayerId = registration.UserId!,
                    Amount = registration.FeeBase
                }
            }
        };

        var actionResult = await controller.ApplyDiscount(request);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<ApplyDiscountResponseDto>(okResult.Value);

        Assert.True(response.Success);
        Assert.True(response.PerPlayer.TryGetValue(registration.UserId!, out var perPlayerDiscount));

        await db.Entry(registration).ReloadAsync();

        var reduction = Math.Round(perPlayerDiscount * (percent / 100m), 2, MidpointRounding.AwayFromZero);
        var expectedProcessing = originalFeeProcessing - reduction;
        var expectedFeeTotal = originalBase + expectedProcessing - perPlayerDiscount - originalDonation;
        if (expectedFeeTotal < 0m) expectedFeeTotal = 0m;
        var expectedOwed = Math.Max(0m, expectedFeeTotal - originalPaid);

        Assert.Equal(expectedProcessing, registration.FeeProcessing);
        Assert.Equal(expectedOwed, registration.OwedTotal);

        // Display before/after tables (single block to avoid duplicated lines from logger)
        var playerName = registration.User != null
            ? $"{registration.User.LastName}, {registration.User.FirstName}"
            : "Unknown";

        string Money(decimal value) => value.ToString("$#,0.00", CultureInfo.InvariantCulture);

        const string header = "{0,-22} {1,12} {2,12} {3,12} {4,14} {5,12} {6,12}";
        const string row = "{0,-22} {1,12} {2,12} {3,12} {4,14} {5,12} {6,12}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{'='} APPLY DISCOUNT TEST {'='}");
        sb.AppendLine();
        sb.AppendLine("[BEFORE] Registration State:");
        sb.AppendLine(string.Format(header, "Player Name", "FeeBase", "FeeDisc", "FeeProc", "FeeTotal", "Paid", "Owed"));
        sb.AppendLine(new string('-', 100));
        sb.AppendLine(string.Format(row, playerName, Money(originalBase), Money(0m), Money(originalFeeProcessing), Money(originalFeeTotal), Money(originalPaid), Money(originalOwed)));
        sb.AppendLine();
        sb.AppendLine($"[ACTION] Now applying discount: {Money(perPlayerDiscount)} (fixed amount) at {percent}% processing fee rate");
        sb.AppendLine($"         Processing fee reduction: {Money(perPlayerDiscount)} × {(percent / 100m):F4} = {Money(reduction)}");
        sb.AppendLine();
        sb.AppendLine("[AFTER] Registration State:");
        sb.AppendLine(string.Format(header, "Player Name", "FeeBase", "FeeDisc", "FeeProc", "FeeTotal", "Paid", "Owed"));
        sb.AppendLine(new string('-', 100));
        sb.AppendLine(string.Format(row, playerName, Money(registration.FeeBase), Money(registration.FeeDiscount), Money(registration.FeeProcessing), Money(registration.FeeTotal), Money(registration.PaidTotal), Money(registration.OwedTotal)));
        sb.AppendLine();
        sb.AppendLine("[VERIFICATION]");
        var processingMatch = registration.FeeProcessing == expectedProcessing ? "✓ PASS" : "✗ FAIL";
        var totalMatch = registration.FeeTotal == expectedFeeTotal ? "✓ PASS" : "✗ FAIL";
        var owedMatch = registration.OwedTotal == expectedOwed ? "✓ PASS" : "✗ FAIL";
        sb.AppendLine($"  Fee Processing: {processingMatch} (expected {Money(expectedProcessing)}, got {Money(registration.FeeProcessing)})");
        sb.AppendLine($"  Fee Total: {totalMatch} (expected {Money(expectedFeeTotal)}, got {Money(registration.FeeTotal)})");
        sb.AppendLine($"  Owed Total: {owedMatch} (expected {Money(expectedOwed)}, got {Money(registration.OwedTotal)})");

        var report = sb.ToString();
        Console.WriteLine(report);
        _output.WriteLine(report);
        await tx.RollbackAsync();
    }

    private static ServiceProvider BuildProvider()
    {
        var connectionString = Environment.GetEnvironmentVariable("TSIC_TEST_CONNECTION_STRING") ?? DefaultConnection;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Fees:CreditCardPercent"] = "3.5"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        services.AddDbContext<SqlDbContext>(options =>
            options.UseSqlServer(connectionString, x => x.UseNetTopologySuite()));

        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IRegistrationRepository, RegistrationRepository>();
        services.AddScoped<IJobDiscountCodeRepository, JobDiscountCodeRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IJobLookupService, JobLookupService>();
        services.AddScoped<ITeamLookupService, TeamLookupService>();
        services.AddScoped<IRegistrationRecordFeeCalculatorService, RegistrationRecordFeeCalculatorService>();
        services.AddScoped<IRegistrationFeeAdjustmentService, RegistrationFeeAdjustmentService>();
        services.AddScoped<IPaymentService, NoopPaymentService>();

        return services.BuildServiceProvider();
    }

    private static PlayerRegistrationPaymentController BuildController(IServiceScope scope, string jobPath, string familyUserId)
    {
        var jobLookup = scope.ServiceProvider.GetRequiredService<IJobLookupService>();
        var payment = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        var discountRepo = scope.ServiceProvider.GetRequiredService<IJobDiscountCodeRepository>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IRegistrationRepository>();
        var feeCalc = scope.ServiceProvider.GetRequiredService<IRegistrationRecordFeeCalculatorService>();
        var feeAdjust = scope.ServiceProvider.GetRequiredService<IRegistrationFeeAdjustmentService>();

        var controller = new PlayerRegistrationPaymentController(
            jobLookup,
            payment,
            discountRepo,
            regRepo,
            feeCalc,
            feeAdjust,
            NullLogger<PlayerRegistrationPaymentController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, familyUserId),
                    new Claim("jobPath", jobPath)
                }, "Test"))
            }
        };

        return controller;
    }

    private sealed class NoopPaymentService : IPaymentService
    {
        public Task<PaymentResponseDto> ProcessPaymentAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId)
        {
            return Task.FromResult(new PaymentResponseDto { Success = false, Message = "Not implemented in test" });
        }

        public Task<TeamPaymentResponseDto> ProcessTeamPaymentAsync(Guid regId, string userId, IReadOnlyCollection<Guid> teamIds, decimal totalAmount, CreditCardInfo creditCard)
        {
            return Task.FromResult(new TeamPaymentResponseDto { Success = false, Message = "Not implemented in test" });
        }
    }
}