using FluentAssertions;
using TSIC.Contracts.Payments;
using Xunit;

namespace TSIC.Tests.Payments;

/// <summary>
/// Pins the single canonical fee formula:
/// FeeTotal = base + proc − discount − discountMp + donation + late.
/// Decisions documented here: donation ADDS, late fee ADDS, OwedTotal stays signed, and
/// BOTH discount buckets SUBTRACT — <c>FeeDiscount</c> (early-bird + discount code) and
/// <c>FeeDiscountMp</c> (reserved for a multi-player/sibling discount; 0.00 on every row today,
/// but wired through so the feature can be built without re-opening this formula).
/// </summary>
public class FeeMathTests
{
    [Fact(DisplayName = "ComputeFeeTotal: base + proc − disc − discMp + donation + late")]
    public void ComputeFeeTotal_FullFormula()
    {
        // 500 + 19 − 50 − 10 + 25 + 15 = 499
        FeeMath.ComputeFeeTotal(
            feeBase: 500m, feeProcessing: 19m, feeDiscount: 50m, feeDiscountMp: 10m,
            feeDonation: 25m, feeLatefee: 15m)
            .Should().Be(499m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: all zero → zero")]
    public void ComputeFeeTotal_AllZero()
    {
        FeeMath.ComputeFeeTotal(0m, 0m, 0m, 0m, 0m, 0m).Should().Be(0m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: discount subtracts")]
    public void ComputeFeeTotal_DiscountSubtracts()
    {
        FeeMath.ComputeFeeTotal(100m, 0m, 30m, 0m, 0m, 0m).Should().Be(70m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: FeeDiscountMp SUBTRACTS (multi-player/sibling bucket)")]
    public void ComputeFeeTotal_FeeDiscountMpSubtracts()
    {
        FeeMath.ComputeFeeTotal(100m, 0m, 0m, 30m, 0m, 0m).Should().Be(70m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: the two discount buckets STACK")]
    public void ComputeFeeTotal_BothDiscountsStack()
    {
        // Early-bird 20 + sibling 30 both come off the same base.
        FeeMath.ComputeFeeTotal(100m, 0m, 20m, 30m, 0m, 0m).Should().Be(50m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: donation ADDS to the total")]
    public void ComputeFeeTotal_DonationAdds()
    {
        FeeMath.ComputeFeeTotal(100m, 0m, 0m, 0m, 30m, 0m).Should().Be(130m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: late fee ADDS to the total")]
    public void ComputeFeeTotal_LateFeeAdds()
    {
        FeeMath.ComputeFeeTotal(100m, 0m, 0m, 0m, 0m, 40m).Should().Be(140m);
    }

    [Fact(DisplayName = "ComputeOwed: feeTotal − paidTotal")]
    public void ComputeOwed_Subtracts()
    {
        FeeMath.ComputeOwed(509m, 200m).Should().Be(309m);
    }

    [Fact(DisplayName = "ComputeOwed: overpayment stays signed (negative, not clamped)")]
    public void ComputeOwed_Overpayment_IsNegative()
    {
        FeeMath.ComputeOwed(100m, 150m).Should().Be(-50m);
    }
}
