using FluentAssertions;
using TSIC.Contracts.Extensions;
using TSIC.Domain.Entities;
using Xunit;

namespace TSIC.Tests.Payments;

/// <summary>
/// Pins the entity RecalcTotals helpers (Registrations + Teams) to the single FeeMath
/// formula: they set FeeTotal/OwedTotal from components, SUBTRACT both discount buckets
/// (FeeDiscount and FeeDiscountMp), keep OwedTotal signed, and coalesce Teams' nullable
/// columns to zero. Also pins TotalDiscount() — the one expression every charge/owed/rating
/// basis must net off, so no call site can drift back to FeeDiscount alone.
/// </summary>
public class RecalcTotalsTests
{
    [Fact(DisplayName = "Registration.RecalcTotals: FeeTotal=base+proc-disc-discMp+don+late, OwedTotal=fee-paid")]
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

    [Fact(DisplayName = "Registration.RecalcTotals: FeeDiscountMp SUBTRACTS from the total")]
    public void Registration_RecalcTotals_SubtractsFeeDiscountMp()
    {
        var reg = new Registrations
        {
            FeeBase = 500m,
            FeeProcessing = 19m,
            FeeDiscount = 50m,
            FeeDonation = 25m,
            FeeLatefee = 15m,
            FeeDiscountMp = 100m, // multi-player/sibling bucket — a real discount
            PaidTotal = 200m,
        };

        reg.RecalcTotals();

        reg.FeeTotal.Should().Be(409m);  // 509 − 100
        reg.OwedTotal.Should().Be(209m);
    }

    [Fact(DisplayName = "Registration.TotalDiscount: both buckets sum — the amount FeeMath subtracts")]
    public void Registration_TotalDiscount_SumsBothBuckets()
    {
        var reg = new Registrations { FeeDiscount = 50m, FeeDiscountMp = 100m };

        reg.TotalDiscount().Should().Be(150m);

        // The contract that matters: FeeTotal must move by exactly TotalDiscount().
        var undiscounted = new Registrations { FeeBase = 500m };
        undiscounted.RecalcTotals();
        var discounted = new Registrations { FeeBase = 500m, FeeDiscount = 50m, FeeDiscountMp = 100m };
        discounted.RecalcTotals();

        (undiscounted.FeeTotal - discounted.FeeTotal).Should().Be(discounted.TotalDiscount());
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

    [Fact(DisplayName = "Team.RecalcTotals: FeeDiscountMp SUBTRACTS from the total")]
    public void Team_RecalcTotals_SubtractsFeeDiscountMp()
    {
        var team = new Teams
        {
            FeeBase = 500m,
            FeeProcessing = 19m,
            FeeDiscount = 50m,
            FeeDonation = 25m,
            FeeLatefee = 15m,
            FeeDiscountMp = 100m,
            PaidTotal = 200m,
        };

        team.RecalcTotals();

        team.FeeTotal.Should().Be(409m);  // 509 − 100
        team.OwedTotal.Should().Be(209m);
    }

    [Fact(DisplayName = "Team.TotalDiscount: both buckets sum, nulls coalesce to zero")]
    public void Team_TotalDiscount_SumsBothBucketsAndCoalesces()
    {
        new Teams { FeeDiscount = 50m, FeeDiscountMp = 100m }.TotalDiscount().Should().Be(150m);
        new Teams { FeeDiscount = 50m, FeeDiscountMp = null }.TotalDiscount().Should().Be(50m);
        new Teams().TotalDiscount().Should().Be(0m);
    }
}
