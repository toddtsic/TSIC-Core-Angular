using FluentAssertions;
using TSIC.Contracts.Payments;
using Xunit;

namespace TSIC.Tests.Payments;

public class PaymentRateMathTests
{
    [Fact(DisplayName = "NonProcCheckCredit: principal × ccRate")]
    public void NonProcCheckCredit_ReturnsPrincipalTimesRate()
    {
        PaymentRateMath.NonProcCheckCredit(500m, 0.038m).Should().Be(19.0m);
    }

    [Fact(DisplayName = "NonProcCheckCredit: zero principal returns zero")]
    public void NonProcCheckCredit_ZeroPrincipal_ReturnsZero()
    {
        PaymentRateMath.NonProcCheckCredit(0m, 0.038m).Should().Be(0m);
    }

    [Fact(DisplayName = "EcheckPartialCredit: principal × (cc − echeck)")]
    public void EcheckPartialCredit_ReturnsRateDifference()
    {
        PaymentRateMath.EcheckPartialCredit(500m, 0.038m, 0.01m).Should().Be(14.0m);
    }

    [Fact(DisplayName = "EcheckPartialCredit: echeckRate ≥ ccRate returns 0")]
    public void EcheckPartialCredit_EcheckGreaterOrEqual_ReturnsZero()
    {
        PaymentRateMath.EcheckPartialCredit(500m, 0.01m, 0.01m).Should().Be(0m);
        PaymentRateMath.EcheckPartialCredit(500m, 0.01m, 0.02m).Should().Be(0m);
    }
}
