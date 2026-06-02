using FluentAssertions;
using TSIC.Contracts.Payments;
using Xunit;

namespace TSIC.Tests.Payments;

/// <summary>
/// Pins the single canonical fee formula: FeeTotal = base + proc − discount + donation + late.
/// Decisions documented here: donation ADDS, late fee ADDS, OwedTotal stays signed, and
/// FeeDiscountMp is intentionally EXCLUDED (retired multi-player discount; the field is kept
/// as a stub on the entities but is not a factor — so it is not even a parameter of the formula).
/// </summary>
public class FeeMathTests
{
    [Fact(DisplayName = "ComputeFeeTotal: base + proc − disc + donation + late")]
    public void ComputeFeeTotal_FullFormula()
    {
        // 500 + 19 − 50 + 25 + 15 = 509
        FeeMath.ComputeFeeTotal(
            feeBase: 500m, feeProcessing: 19m, feeDiscount: 50m,
            feeDonation: 25m, feeLatefee: 15m)
            .Should().Be(509m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: all zero → zero")]
    public void ComputeFeeTotal_AllZero()
    {
        FeeMath.ComputeFeeTotal(0m, 0m, 0m, 0m, 0m).Should().Be(0m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: discount subtracts")]
    public void ComputeFeeTotal_DiscountSubtracts()
    {
        FeeMath.ComputeFeeTotal(100m, 0m, 30m, 0m, 0m).Should().Be(70m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: donation ADDS to the total")]
    public void ComputeFeeTotal_DonationAdds()
    {
        FeeMath.ComputeFeeTotal(100m, 0m, 0m, 30m, 0m).Should().Be(130m);
    }

    [Fact(DisplayName = "ComputeFeeTotal: late fee ADDS to the total")]
    public void ComputeFeeTotal_LateFeeAdds()
    {
        FeeMath.ComputeFeeTotal(100m, 0m, 0m, 0m, 40m).Should().Be(140m);
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
