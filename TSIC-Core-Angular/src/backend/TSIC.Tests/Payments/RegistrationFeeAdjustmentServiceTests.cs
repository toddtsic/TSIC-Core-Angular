using FluentAssertions;
using Moq;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Payments;

/// <summary>
/// REGISTRATION FEE ADJUSTMENT — INVARIANT TESTS
///
/// These exercise the REAL <see cref="RegistrationFeeAdjustmentService"/> (only IJobRepository and
/// IFeeResolutionService are mocked). They pin the invariant the prior code violated: a Reduce/Reverse
/// changed FeeProcessing and hand-decremented OwedTotal but never touched FeeTotal, leaving it stale.
///
/// After the fix each method mutates the component then calls RecalcTotals(), so:
///   • FeeTotal stays equal to its components (no stale value), and
///   • OwedTotal is the SIGNED FeeTotal − PaidTotal (no Math.Max(0,…) clamp), so an overpayment
///     created by a reduction surfaces as a negative owed instead of being hidden as $0.
///
/// The ARB-Trial / eCheck payment suites MOCK this service, so the real RecalcTotals never runs there —
/// without these tests the fix is unverified. PaymentService's team-eCheck charge reads the FeeTotal
/// that <see cref="RegistrationFeeAdjustmentService.ReduceTeamProcessingFeeForEcheckAsync"/> leaves,
/// so the last test is the direct guard for that charge being the eCheck-reduced amount.
/// </summary>
public class RegistrationFeeAdjustmentServiceTests
{
    private const decimal CcRate = 0.035m;
    private const decimal EcheckRate = 0.02m;
    private static readonly Guid JobId = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000001");

    private static RegistrationFeeAdjustmentService CreateService()
    {
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = true,
                BApplyProcessingFeesToTeamDeposit = false,
                BTeamsFullPaymentRequired = false,
                PaymentMethodsAllowedCode = 7
            });

        var feeService = new Mock<IFeeResolutionService>();
        feeService.Setup(f => f.GetEffectiveProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CcRate);
        feeService.Setup(f => f.GetEffectiveEcheckProcessingRateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EcheckRate);

        return new RegistrationFeeAdjustmentService(jobRepo.Object, feeService.Object);
    }

    private static Registrations Reg(decimal feeBase, decimal feeProcessing, decimal paidTotal)
    {
        var feeTotal = feeBase + feeProcessing;
        return new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = JobId,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = 0m,
            FeeDonation = 0m,
            FeeLatefee = 0m,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = feeTotal - paidTotal,
            Modified = DateTime.Now
        };
    }

    [Fact(DisplayName = "Reduce: FeeTotal tracks the reduced FeeProcessing (not left stale)")]
    public async Task Reduce_KeepsFeeTotalConsistentWithComponents()
    {
        var svc = CreateService();
        var reg = Reg(feeBase: 595m, feeProcessing: 20.83m, paidTotal: 0m);

        var reduced = await svc.ReduceProcessingFeeProportionalAsync(reg, adjustmentAmount: 100m, JobId, "user");

        reduced.Should().BeGreaterThan(0m);
        reg.FeeProcessing.Should().Be(20.83m - reduced);
        // The fix: FeeTotal moves with the component. Pre-fix it stayed at 615.83 (stale).
        reg.FeeTotal.Should().Be(reg.FeeBase + reg.FeeProcessing);
        reg.OwedTotal.Should().Be(reg.FeeTotal - reg.PaidTotal);
    }

    [Fact(DisplayName = "Reduce on a paid-in-full reg yields a SIGNED negative owed (overpayment surfaced, not clamped)")]
    public async Task Reduce_PaidInFull_SurfacesOverpaymentAsNegativeOwed()
    {
        var svc = CreateService();
        // Paid exactly in full before the reduction.
        var reg = Reg(feeBase: 100m, feeProcessing: 3.50m, paidTotal: 103.50m);
        reg.OwedTotal.Should().Be(0m);

        var reduced = await svc.ReduceProcessingFeeProportionalAsync(reg, adjustmentAmount: 100m, JobId, "user");

        reduced.Should().BeGreaterThan(0m);
        // Reduction drops FeeTotal below PaidTotal → the family is now owed a refund.
        reg.FeeTotal.Should().Be(reg.FeeBase + reg.FeeProcessing);
        reg.OwedTotal.Should().Be(reg.FeeTotal - reg.PaidTotal);
        reg.OwedTotal.Should().BeNegative();   // pre-fix: Math.Max(0, …) hid this as $0
        reg.OwedTotal.Should().Be(-reduced);
    }

    [Fact(DisplayName = "Reverse: restores FeeProcessing AND FeeTotal together")]
    public async Task Reverse_RestoresFeeTotalWithProcessing()
    {
        var svc = CreateService();
        // A reg whose proc was previously eCheck-credited down.
        var reg = Reg(feeBase: 100m, feeProcessing: 2.00m, paidTotal: 0m);

        var restored = await svc.ReverseProcessingFeeForEcheckAsync(reg, echeckAmount: 100m, JobId, "user");

        restored.Should().BeGreaterThan(0m);
        reg.FeeProcessing.Should().Be(2.00m + restored);
        reg.FeeTotal.Should().Be(reg.FeeBase + reg.FeeProcessing);   // pre-fix: stale at 102.00
        reg.OwedTotal.Should().Be(reg.FeeTotal - reg.PaidTotal);
    }

    [Fact(DisplayName = "Team eCheck reduce: FeeTotal tracks reduced proc (the PaymentService charge reads this)")]
    public async Task TeamEcheckReduce_KeepsFeeTotalConsistent()
    {
        var svc = CreateService();
        var team = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = JobId,
            FeeBase = 2000m,
            FeeProcessing = 70m,
            FeeDiscount = 0m,
            FeeDonation = 0m,
            FeeLatefee = 0m,
            FeeTotal = 2070m,
            PaidTotal = 0m,
            OwedTotal = 2070m,
            PerRegistrantDeposit = 0m,   // not a deposit scenario → proc capped at current, no negative
            Modified = DateTime.Now
        };

        var reduced = await svc.ReduceTeamProcessingFeeForEcheckAsync(team, echeckAmount: 2000m, JobId, "user");

        reduced.Should().BeGreaterThan(0m);
        (team.FeeProcessing ?? 0m).Should().Be(70m - reduced);
        // The fix that makes PaymentService's team-eCheck charge bill the reduced amount.
        (team.FeeTotal ?? 0m).Should().Be((team.FeeBase ?? 0m) + (team.FeeProcessing ?? 0m));
        (team.OwedTotal ?? 0m).Should().Be((team.FeeTotal ?? 0m) - (team.PaidTotal ?? 0m));
    }
}
