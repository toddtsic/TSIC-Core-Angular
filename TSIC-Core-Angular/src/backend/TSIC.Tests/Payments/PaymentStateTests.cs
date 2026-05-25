using FluentAssertions;
using TSIC.Contracts.Payments;
using Xunit;

namespace TSIC.Tests.Payments;

/// <summary>
/// Validates the canonical formulas on PaymentState. These derived properties
/// are the single source of truth — both recalc paths and display paths consume
/// them, so any drift here surfaces immediately as failing recalc/display tests.
/// </summary>
public class PaymentStateTests
{
    private static PaymentState State(
        decimal cc = 0m, decimal echeck = 0m, decimal check = 0m,
        decimal cash = 0m, decimal correction = 0m,
        bool bAdd = true, decimal ccRate = 0.038m, decimal echeckRate = 0.01m) =>
        new()
        {
            CcGrossPaid = cc,
            EcheckGrossPaid = echeck,
            CheckPaid = check,
            CashPaid = cash,
            CorrectionApplied = correction,
            BAddProcessingFees = bAdd,
            CcRate = ccRate,
            EcheckRate = echeckRate,
        };

    // ── Reverse-out math ──

    [Fact(DisplayName = "CC reverse-out: gross / (1 + ccRate) when proc enabled")]
    public void CcPrincipalPaid_ProcEnabled_ReversesOut()
    {
        var s = State(cc: 467.10m, ccRate: 0.038m);
        s.CcPrincipalPaid.Should().BeApproximately(450m, 0.005m);
        s.CcProcCollected.Should().BeApproximately(17.10m, 0.005m);
    }

    [Fact(DisplayName = "CC reverse-out: full amount is principal when proc disabled")]
    public void CcPrincipalPaid_ProcDisabled_FullPrincipal()
    {
        var s = State(cc: 500m, bAdd: false);
        s.CcPrincipalPaid.Should().Be(500m);
        s.CcProcCollected.Should().Be(0m);
    }

    [Fact(DisplayName = "eCheck proc collected = principal × echeckRate")]
    public void EcheckProcCollected_AppliedAtEcheckRate()
    {
        // Gross $505 stored; principal reverses to $500, proc = $5.00 at 1% (symmetric with CC).
        var s = State(echeck: 505m, echeckRate: 0.01m);
        s.EcheckProcCollected.Should().Be(5.00m);
    }

    [Fact(DisplayName = "eCheck proc collected = 0 when proc disabled")]
    public void EcheckProcCollected_ProcDisabled_Zero()
    {
        var s = State(echeck: 500m, bAdd: false);
        s.EcheckProcCollected.Should().Be(0m);
    }

    // ── Aggregates ──

    [Fact(DisplayName = "PrincipalPaid sums all method principals")]
    public void PrincipalPaid_SumsAllMethods()
    {
        // CC $467.10→$450 principal; eCheck gross $202→$200 principal; check $100; correction $50.
        var s = State(cc: 467.10m, echeck: 202m, check: 100m, correction: 50m, ccRate: 0.038m);
        s.PrincipalPaid.Should().BeApproximately(450m + 200m + 100m + 50m, 0.005m);
    }

    [Fact(DisplayName = "GrossPaid mirrors entity.PaidTotal accumulator")]
    public void GrossPaid_MirrorsEntityPaidTotal()
    {
        var s = State(cc: 467.10m, echeck: 200m, check: 100m, correction: 50m);
        s.GrossPaid.Should().Be(467.10m + 200m + 100m + 50m);
    }

    // ── FeeProcessingTarget invariant ──
    //
    //   FeeProcessingTarget = ProcCollected + remainingCcBillable × CcRate
    //
    // After paying the full base by mixed methods, target = total proc collected
    // so far + proc on whatever principal is still outstanding (if eventually CC).

    [Fact(DisplayName = "Target on pristine entity = base × ccRate")]
    public void Target_NoPayments_BaseTimesRate()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.FeeProcessingTarget(1650m, 0m, 0m).Should().BeApproximately(62.70m, 0.005m);
    }

    [Fact(DisplayName = "Target after $500 check: shrinks by $500 × ccRate")]
    public void Target_AfterCheck_ShrinksFully()
    {
        var s = State(check: 500m);
        s.FeeProcessingTarget(1650m, 0m, 0m)
            .Should().BeApproximately((1650m - 500m) * 0.038m, 0.005m);   // 43.70
    }

    [Fact(DisplayName = "Target after $500 eCheck: collected proc + remaining × ccRate (the bug regression)")]
    public void Target_AfterEcheck_PreservesCollectedProc()
    {
        // eCheck gross $505 → $500 principal + $5 proc collected at swipe; CC target on
        // remaining $1150 = $43.70. Total lifetime FeeProcessing target = $48.70. The OLD
        // recalc lumped eCheck into "non-CC" and subtracted only the principal — it
        // computed $43.70 and lost the $5 already-collected eCheck proc credit on recalc.
        var s = State(echeck: 505m);
        s.FeeProcessingTarget(1650m, 0m, 0m)
            .Should().BeApproximately(48.70m, 0.005m);
    }

    [Fact(DisplayName = "Target after CC payment: unchanged (CC pays its own proc share)")]
    public void Target_AfterCcPayment_Unchanged()
    {
        // CC paid $450 principal + $17.10 proc. Target stays $62.70:
        //   ProcCollected $17.10 + remaining $1200 × 0.038 = $17.10 + $45.60 = $62.70.
        var s = State(cc: 467.10m);
        s.FeeProcessingTarget(1650m, 0m, 0m)
            .Should().BeApproximately(62.70m, 0.005m);
    }

    [Fact(DisplayName = "Target with proc disabled: always zero")]
    public void Target_ProcDisabled_Zero()
    {
        var s = State(check: 500m, bAdd: false, ccRate: 0m);
        s.FeeProcessingTarget(1650m, 0m, 0m).Should().Be(0m);
    }

    // ── Display: PrincipalRemaining + ProcFeeDue ──

    [Fact(DisplayName = "PrincipalRemaining = base − all paid principal")]
    public void PrincipalRemaining_AllMethodsCounted()
    {
        var s = State(cc: 467.10m, check: 200m, ccRate: 0.038m);
        // CC principal = 450, check = 200, total paid principal = 650.
        s.PrincipalRemaining(1650m, 0m, 0m).Should().BeApproximately(1000m, 0.005m);
    }

    [Fact(DisplayName = "ProcFeeDue = principal-remaining × ccRate (the team display bug fix)")]
    public void ProcFeeDue_AfterCcDeposit_ReflectsRemainingPrincipalOnly()
    {
        // FeeBase $2100, $450 paid principal via CC. Remaining principal $1650.
        // ProcFee Due = $1650 × .038 = $62.70 — what Ann's screenshot wanted.
        var s = State(cc: 467.10m, ccRate: 0.038m);
        s.ProcFeeDue(2100m, 0m, 0m).Should().BeApproximately(62.70m, 0.005m);
    }

    [Fact(DisplayName = "PrincipalRemaining clamps at 0 (overpayment)")]
    public void PrincipalRemaining_OverPaid_ClampsAtZero()
    {
        var s = State(check: 2000m);
        s.PrincipalRemaining(1500m, 0m, 0m).Should().Be(0m);
    }

    // ── Discount + late fee threading ──

    [Fact(DisplayName = "Discount reduces principal base; lateFee adds to it")]
    public void DiscountAndLateFee_ApplyToPrincipalBase()
    {
        var s = State(check: 100m);
        // base = 1000 − 50 + 25 = 975, paid 100 → remaining 875; proc due = 875 × .038
        s.PrincipalRemaining(1000m, 50m, 25m).Should().BeApproximately(875m, 0.005m);
        s.ProcFeeDue(1000m, 50m, 25m).Should().BeApproximately(875m * 0.038m, 0.005m);
    }

    // ── ResolveOwed: the single per-method owed resolver ──

    [Fact(DisplayName = "ResolveOwed: pristine team — CC full, check drops CC proc, eCheck drops only the rate difference")]
    public void ResolveOwed_NoPayments_DropsCcProcForCheck()
    {
        // base 500, embedded proc 19 (500 × 3.8%), owed 519, nothing paid.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var owed = s.ResolveOwed(owedTotal: 519m, feeBase: 500m, discount: 0m, lateFee: 0m, feeProcessing: 19m);

        owed.Cc.Should().BeApproximately(519m, 0.005m);     // full CC-inclusive owed
        owed.Check.Should().BeApproximately(500m, 0.005m);  // proc removed → principal only
        owed.Echeck.Should().BeApproximately(505m, 0.005m); // base + eCheck proc (500 × 1%)
    }

    [Fact(DisplayName = "ResolveOwed: proc disabled — every method equals the raw owed")]
    public void ResolveOwed_ProcDisabled_AllMethodsEqualOwed()
    {
        var s = PaymentState.Empty(false, 0.038m, 0.01m);
        var owed = s.ResolveOwed(owedTotal: 500m, feeBase: 500m, discount: 0m, lateFee: 0m, feeProcessing: 0m);

        owed.Cc.Should().Be(500m);
        owed.Check.Should().Be(500m);
        owed.Echeck.Should().Be(500m);
    }

    [Fact(DisplayName = "ResolveOwed: after a partial CC deposit — check owes remaining principal, no phantom proc")]
    public void ResolveOwed_AfterPartialCc_ChecksRemainingPrincipal()
    {
        // base 2100; CC deposit $467.10 = $450 principal + $17.10 proc.
        // Embedded FeeProcessing target = 17.10 + 1650 × 3.8% = 79.80; owed = 2179.80 − 467.10 = 1712.70.
        var s = State(cc: 467.10m, ccRate: 0.038m, echeckRate: 0.01m);
        var owed = s.ResolveOwed(owedTotal: 1712.70m, feeBase: 2100m, discount: 0m, lateFee: 0m, feeProcessing: 79.80m);

        owed.Cc.Should().BeApproximately(1712.70m, 0.005m);
        owed.Check.Should().BeApproximately(1650m, 0.005m);     // remaining principal only
        owed.Echeck.Should().BeApproximately(1666.50m, 0.005m); // 1650 + 1650 × 1%
    }

    [Fact(DisplayName = "ResolveOwed: a donation stays owed under check (anchored to owedTotal, not bare principal)")]
    public void ResolveOwed_DonationPreservedForCheck()
    {
        // base 500 + donation 10 + proc 19 → owed 529. Check drops the 19 proc but KEEPS the donation.
        // (Guards the latent bug where check-owed = PrincipalRemaining would silently drop the donation.)
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var owed = s.ResolveOwed(owedTotal: 529m, feeBase: 500m, discount: 0m, lateFee: 0m, feeProcessing: 19m);

        owed.Check.Should().BeApproximately(510m, 0.005m); // 500 base + 10 donation, proc removed
    }

    [Fact(DisplayName = "ResolveOwed: nothing owed clamps every method at 0")]
    public void ResolveOwed_NothingOwed_ClampsAtZero()
    {
        var s = State(check: 600m);
        var owed = s.ResolveOwed(owedTotal: 0m, feeBase: 500m, discount: 0m, lateFee: 0m, feeProcessing: 0m);

        owed.Cc.Should().Be(0m);
        owed.Check.Should().Be(0m);
        owed.Echeck.Should().Be(0m);
    }
}
