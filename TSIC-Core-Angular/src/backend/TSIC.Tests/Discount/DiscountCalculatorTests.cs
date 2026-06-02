using FluentAssertions;
using TSIC.Application.Services.Shared.Discount;

namespace TSIC.Tests.Discount;

/// <summary>
/// Unit tests for the single canonical discount calculator. Both the player
/// (PlayerRegistrationPaymentController) and team (TeamRegistrationController) apply-discount
/// paths route through this, so these tests lock the behavior for both: percent off the base,
/// AwayFromZero rounding, and every discount capped at the base amount.
/// </summary>
public class DiscountCalculatorTests
{
    [Theory(DisplayName = "Percent: discountValue% of baseAmount")]
    [InlineData(595, 50, 297.50)]   // the reported scenario: 50% of $595 base
    [InlineData(200, 50, 100)]      // 50% of $200
    [InlineData(595, 100, 595)]     // 100% → full base
    [InlineData(100, 10, 10)]       // 10% of $100
    public void Percent_AppliesToBase(decimal baseAmount, decimal pct, decimal expected)
    {
        DiscountCalculator.Calculate(baseAmount, pct, isPercentage: true).Should().Be(expected);
    }

    [Fact(DisplayName = "Percent: half-cent rounds AwayFromZero (1.005 → 1.01, not banker's 1.00)")]
    public void Percent_RoundsAwayFromZero()
    {
        // 20.10 * 0.05 = 1.005 exactly. AwayFromZero → 1.01; banker's (ToEven) would give 1.00.
        DiscountCalculator.Calculate(20.10m, 5m, isPercentage: true).Should().Be(1.01m);
    }

    [Fact(DisplayName = "Percent: result capped at base when over 100%")]
    public void Percent_CapsAtBase()
    {
        DiscountCalculator.Calculate(100m, 150m, isPercentage: true).Should().Be(100m);
    }

    [Theory(DisplayName = "Fixed: discountValue, capped at base")]
    [InlineData(595, 100, 100)]     // under base → full code
    [InlineData(100, 100, 100)]     // equal to base
    [InlineData(100, 103.50, 100)]  // over base → capped (team path was previously uncapped)
    [InlineData(100, 200, 100)]     // far over base → capped
    public void Fixed_CapsAtBase(decimal baseAmount, decimal code, decimal expected)
    {
        DiscountCalculator.Calculate(baseAmount, code, isPercentage: false).Should().Be(expected);
    }

    [Theory(DisplayName = "Non-positive base or value → 0")]
    [InlineData(0, 50, true)]
    [InlineData(-5, 50, true)]
    [InlineData(100, 0, true)]
    [InlineData(100, -10, false)]
    public void NonPositiveInputs_ReturnZero(decimal baseAmount, decimal value, bool isPercentage)
    {
        DiscountCalculator.Calculate(baseAmount, value, isPercentage).Should().Be(0m);
    }
}
