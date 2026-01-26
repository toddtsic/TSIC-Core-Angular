using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TSIC.Infrastructure.Data.SqlDbContext;
using Xunit.Abstractions;

namespace TSIC.Tests.Audits;

/// <summary>
/// Audit tests that identify financial discrepancies in real dev database.
/// Specifically looks for discounted registrations that did NOT adjust fee processing.
/// Tests against ACTUAL dev DB, not in-memory.
/// </summary>
public class FeeProcessingAuditTests
{
    private readonly ITestOutputHelper _output;

    public FeeProcessingAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private SqlDbContext GetDbContext()
    {
        // HARDCODED connection string for dev database
        // TODO: Move to environment variable or test config file if needed
        var connectionString = Environment.GetEnvironmentVariable("TSIC_TEST_CONNECTION_STRING")
            ?? "Server=TSIC-SEDONA\\SS2016;Database=TSICV5;Integrated Security=true;TrustServerCertificate=true;";

        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlServer(connectionString, x => x.UseNetTopologySuite()) // Enable spatial types
            .Options;

        return new SqlDbContext(options);
    }

    /// <summary>
    /// Test player registrations with discounts to verify fee processing adjustments.
    /// 
    /// LOGIC:
    /// When a discount is applied, processing fees should be reduced proportionally.
    /// This test identifies registrations where FeeProcessing was NOT adjusted.
    /// Expected reduction = FeeDiscount * (CCPercent / 100)
    /// 
    /// This test audits ONE specific job to prevent overwhelming output.
    /// Always passes (informational), never fails due to missing preconditions.
    /// 
    /// USAGE: Change targetJobId parameter below to audit different jobs.
    /// </summary>
    [Fact]
    public async Task playerDiscounts_testFeeDiscount()
    {
        // ========== PARAMETERS (change jobId to audit different jobs) ==========
        var targetJobId = Guid.Parse("9225D9C2-E64A-498A-8E6B-02270E8F97EB"); // The Players Series:Boys Summer Showcase 2026
        const decimal expectedCCPercent = 3.5m; // Default CC fee percentage

        using var _db = GetDbContext();

        // ========== STEP 1: Verify Job Exists & Get Details ==========
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobId == targetJobId);
        if (job == null)
        {
            _output.WriteLine($"⚠ Job {targetJobId} not found in database.\n");
            return; // Skip, don't fail
        }

        // ========== STEP 2: Query ACTIVE PLAYER registrations with discounts in this job ==========
        var registrations = await _db.Registrations
            .Include(r => r.User)
            .Include(r => r.DiscountCode)
            .AsNoTracking()
            .Where(r => r.JobId == targetJobId
                && r.FeeDiscount > 0
                && r.BActive == true
                && r.Role.Name == "Player"
            )
            .OrderBy(r => r.User!.LastName)
            .ThenBy(r => r.User!.FirstName)
            .ToListAsync();

        if (registrations.Count == 0)
        {
            _output.WriteLine($"⚠ Job '{job.JobName}' has no registrations with discounts.\n");
            return; // Skip, don't fail
        }

        // ========== STEP 3: Identify Missing Fee Processing Adjustments ==========
        var auditResults = new List<AuditResult>();

        foreach (var reg in registrations)
        {
            var expectedReduction = reg.FeeDiscount * (expectedCCPercent / 100m);
            expectedReduction = Math.Round(expectedReduction, 2, MidpointRounding.AwayFromZero);

            var expectedFeeProcessing = CalculateExpectedFeeProcessing(
                reg.FeeBase,
                reg.FeeDiscount,
                expectedCCPercent);

            var actualReduction = 0m; // Production did not reduce - this is what we're documenting
            var missingAdjustment = reg.FeeProcessing - expectedFeeProcessing;

            auditResults.Add(new AuditResult
            {
                RegistrationId = reg.RegistrationId,
                PlayerName = reg.User != null ? $"{reg.User.LastName}, {reg.User.FirstName}" : "UNKNOWN",
                DiscountCode = reg.DiscountCode?.CodeName ?? "N/A",
                FeeDiscount = reg.FeeDiscount,
                PaidTotal = reg.PaidTotal,
                OwedTotal = reg.OwedTotal,
                ExpectedReduction = expectedReduction,
                ActualReduction = actualReduction,
                MissingAdjustment = missingAdjustment
            });
        }

        // ========== STEP 4: Display Table Report ==========
        var adjustedCount = auditResults.Count(r => Math.Abs(r.MissingAdjustment) < 0.01m);
        var unadjustedCount = auditResults.Count - adjustedCount;

        _output.WriteLine($"\n{'='} UNADJUSTED FEE PROCESSING AUDIT {'='}");
        _output.WriteLine($"Job: {job.JobName}");
        _output.WriteLine($"JobId: {job.JobId}");
        _output.WriteLine($"CC Fee %: {expectedCCPercent}%");
        _output.WriteLine($"✓ Adjusted: {adjustedCount} | ✗ Unadjusted: {unadjustedCount}\n");

        if (unadjustedCount > 0)
        {
            _output.WriteLine("Discounted registrations that did NOT adjust fee processing:\n");
            _output.WriteLine($"{"Player Name",-25} {"Disc Code",-12} {"Discount",10} {"Paid",10} {"Owed",10} {"Should Have",12} {"Actual",10} {"Missing",10}");
            _output.WriteLine($"{"           ",-25} {"         ",-12} {"Applied",10} {"Total",10} {"Total",10} {"Reduced By",12} {"Reduction",10} {"Adjustment",10}");
            _output.WriteLine(new string('-', 122));

            foreach (var result in auditResults.Where(r => Math.Abs(r.MissingAdjustment) >= 0.01m))
            {
                _output.WriteLine($"{result.PlayerName,-25} {result.DiscountCode,-12} ${result.FeeDiscount,8:F2} ${result.PaidTotal,8:F2} ${result.OwedTotal,8:F2} ${result.ExpectedReduction,10:F2} ${result.ActualReduction,8:F2} ${result.MissingAdjustment,8:F2}");
            }
        }

        // Test always passes (informational audit, not a gating assertion)
        Assert.True(true);
    }

    /// <summary>
    /// Calculate what FeeProcessing SHOULD be given base fee, discount, and CC percentage.
    /// Formula:
    ///   discountedBase = FeeBase - FeeDiscount
    ///   if discountedBase <= 0, return 0 (full discount covers base)
    ///   else return discountedBase * (CCPercent / 100), rounded to 2 decimals
    /// </summary>
    private decimal CalculateExpectedFeeProcessing(decimal feeBase, decimal feeDiscount, decimal ccPercent)
    {
        var discountedBase = feeBase - feeDiscount;

        if (discountedBase <= 0m)
            return 0m; // Full discount covers base fee, no processing fee

        var expectedProcessing = discountedBase * (ccPercent / 100m);
        return Math.Round(expectedProcessing, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Internal class to hold audit results for unadjusted fee processing
    /// </summary>
    private class AuditResult
    {
        public Guid RegistrationId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string DiscountCode { get; set; } = string.Empty;
        public decimal FeeDiscount { get; set; }
        public decimal PaidTotal { get; set; }
        public decimal OwedTotal { get; set; }
        public decimal ExpectedReduction { get; set; }
        public decimal ActualReduction { get; set; }
        public decimal MissingAdjustment { get; set; }
    }
}
