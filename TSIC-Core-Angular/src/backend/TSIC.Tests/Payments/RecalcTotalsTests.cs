using FluentAssertions;
using TSIC.Contracts.Extensions;
using TSIC.Domain.Entities;
using Xunit;

namespace TSIC.Tests.Payments;

/// <summary>
/// Pins the entity RecalcTotals helpers (Registrations + Teams) to the single FeeMath
/// formula: they set FeeTotal/OwedTotal from components, exclude the retired FeeDiscountMp
/// stub, keep OwedTotal signed, and coalesce Teams' nullable columns to zero.
/// </summary>
public class RecalcTotalsTests
{
    [Fact(DisplayName = "Registration.RecalcTotals: FeeTotal=base+proc-disc+don+late, OwedTotal=fee-paid")]
    public void Registration_RecalcTotals_DerivesBothTotals()
    {
        var reg = new Registrations
        {
            FeeBase = 500m,
            FeeProcessing = 19m,
            FeeDiscount = 50m,
            FeeDonation = 25m,
            FeeLatefee = 15m,
            PaidTotal = 200m,
        };

        reg.RecalcTotals();

        reg.FeeTotal.Should().Be(509m);
        reg.OwedTotal.Should().Be(309m);
    }

    [Fact(DisplayName = "Registration.RecalcTotals: FeeDiscountMp is a stub and does NOT affect totals")]
    public void Registration_RecalcTotals_IgnoresFeeDiscountMp()
    {
        var reg = new Registrations
        {
            FeeBase = 500m,
            FeeProcessing = 19m,
            FeeDiscount = 50m,
            FeeDonation = 25m,
            FeeLatefee = 15m,
            FeeDiscountMp = 999m, // retired concept — must be ignored
            PaidTotal = 200m,
        };

        reg.RecalcTotals();

        reg.FeeTotal.Should().Be(509m); // unchanged by the 999 Mp value
        reg.OwedTotal.Should().Be(309m);
    }

    [Fact(DisplayName = "Registration.RecalcTotals: overpayment leaves OwedTotal signed (negative)")]
    public void Registration_RecalcTotals_OverpaymentIsNegative()
    {
        var reg = new Registrations { FeeBase = 100m, PaidTotal = 150m };

        reg.RecalcTotals();

        reg.FeeTotal.Should().Be(100m);
        reg.OwedTotal.Should().Be(-50m);
    }

    [Fact(DisplayName = "Team.RecalcTotals: all-null fee columns coalesce to zero totals")]
    public void Team_RecalcTotals_NullColumnsCoalesceToZero()
    {
        var team = new Teams();

        team.RecalcTotals();

        team.FeeTotal.Should().Be(0m);
        team.OwedTotal.Should().Be(0m);
    }

    [Fact(DisplayName = "Team.RecalcTotals: derives totals and ignores the FeeDiscountMp stub")]
    public void Team_RecalcTotals_DerivesTotalsAndIgnoresMp()
    {
        var team = new Teams
        {
            FeeBase = 500m,
            FeeProcessing = 19m,
            FeeDiscount = 50m,
            FeeDonation = 25m,
            FeeLatefee = 15m,
            FeeDiscountMp = 999m, // retired concept — must be ignored
            PaidTotal = 200m,
        };

        team.RecalcTotals();

        team.FeeTotal.Should().Be(509m);
        team.OwedTotal.Should().Be(309m);
    }
}
