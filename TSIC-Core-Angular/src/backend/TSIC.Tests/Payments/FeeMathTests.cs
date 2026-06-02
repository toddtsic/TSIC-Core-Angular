using FluentAssertions;
using TSIC.Contracts.Payments;
using Xunit;

namespace TSIC.Tests.Payments;

/// <summary>
/// Pins the single canonical fee formula. These tests document the decisions that the
/// shadow interceptor and (later) the derive-stage chokepoint enforce everywhere:
/// DiscountMp is subtracted, donation ADDS to the total, late fee ADDS, and OwedTotal
/// stays signed (overpayment is negative, never clamped here).
/// </summary>
public class FeeMathTests
{
    [Fact(DisplayName = "ComputeFeeTotal: base + proc − disc − discMp + donation + late")]
    public void ComputeFeeTotal_FullFormula()
    {
        // 500 + 19 − 50 − 10 + 25 + 15 = 499
        FeeMath.ComputeFeeTotal(
            feeBase: 500m, feeProcessing: 19m, feeDiscount: 50m,
            feeDiscountMp: 10m, feeDonation: 25m, feeLatefee: 15m)
            .Should().Be(499m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: all zero → zero")]
    public void ComputeFeeTotal_AllZero()
    {
        FeeMath.ComputeFeeTotal(0m, 0m, 0m, 0m, 0m, 0m).Should().Be(0m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: FeeDiscountMp is subtracted (not ignored)")]
    public void ComputeFeeTotal_DiscountMpSubtracts()
    {
        // 100 − 10 (disc) − 20 (discMp) = 70
        FeeMath.ComputeFeeTotal(100m, 0m, 10m, 20m, 0m, 0m).Should().Be(70m);
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
        FeeMath.ComputeOwed(499m, 200m).Should().Be(299m);
    }

    [Fact(DisplayName = "ComputeOwed: overpayment stays signed (negative, not clamped)")]
    public void ComputeOwed_Overpayment_IsNegative()
    {
        FeeMath.ComputeOwed(100m, 150m).Should().Be(-50m);
    }
}
