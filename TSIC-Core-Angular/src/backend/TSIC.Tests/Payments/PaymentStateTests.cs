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
        s.FeeProcessingTarget(1650m, 0m, 0m, 0m).Should().BeApproximately(62.70m, 0.005m);
    }

    [Fact(DisplayName = "Target after $500 check: shrinks by $500 × ccRate")]
    public void Target_AfterCheck_ShrinksFully()
    {
        var s = State(check: 500m);
        s.FeeProcessingTarget(1650m, 0m, 0m, 0m)
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
        s.FeeProcessingTarget(1650m, 0m, 0m, 0m)
            .Should().BeApproximately(48.70m, 0.005m);
    }

    [Fact(DisplayName = "Target after CC payment: unchanged (CC pays its own proc share)")]
    public void Target_AfterCcPayment_Unchanged()
    {
        // CC paid $450 principal + $17.10 proc. Target stays $62.70:
        //   ProcCollected $17.10 + remaining $1200 × 0.038 = $17.10 + $45.60 = $62.70.
        var s = State(cc: 467.10m);
        s.FeeProcessingTarget(1650m, 0m, 0m, 0m)
            .Should().BeApproximately(62.70m, 0.005m);
    }

    [Fact(DisplayName = "Target with proc disabled: always zero")]
    public void Target_ProcDisabled_Zero()
    {
        var s = State(check: 500m, bAdd: false, ccRate: 0m);
        s.FeeProcessingTarget(1650m, 0m, 0m, 0m).Should().Be(0m);
    }

    // ── Display: PrincipalRemaining + ProcFeeDue ──

    [Fact(DisplayName = "PrincipalRemaining = base − all paid principal")]
    public void PrincipalRemaining_AllMethodsCounted()
    {
        var s = State(cc: 467.10m, check: 200m, ccRate: 0.038m);
        // CC principal = 450, check = 200, total paid principal = 650.
        s.PrincipalRemaining(1650m, 0m, 0m, 0m).Should().BeApproximately(1000m, 0.005m);
    }

    [Fact(DisplayName = "ProcFeeDue = principal-remaining × ccRate (the team display bug fix)")]
    public void ProcFeeDue_AfterCcDeposit_ReflectsRemainingPrincipalOnly()
    {
        // FeeBase $2100, $450 paid principal via CC. Remaining principal $1650.
        // ProcFee Due = $1650 × .038 = $62.70 — what Ann's screenshot wanted.
        var s = State(cc: 467.10m, ccRate: 0.038m);
        s.ProcFeeDue(2100m, 0m, 0m, 0m).Should().BeApproximately(62.70m, 0.005m);
    }

    [Fact(DisplayName = "PrincipalRemaining clamps at 0 (overpayment)")]
    public void PrincipalRemaining_OverPaid_ClampsAtZero()
    {
        var s = State(check: 2000m);
        s.PrincipalRemaining(1500m, 0m, 0m, 0m).Should().Be(0m);
    }

    // ── Discount + late fee threading ──

    [Fact(DisplayName = "Discount reduces principal base; lateFee adds to it")]
    public void DiscountAndLateFee_ApplyToPrincipalBase()
    {
        var s = State(check: 100m);
        // base = 1000 − 50 + 25 = 975, paid 100 → remaining 875; proc due = 875 × .038
        s.PrincipalRemaining(1000m, 50m, 25m, 0m).Should().BeApproximately(875m, 0.005m);
        s.ProcFeeDue(1000m, 50m, 25m, 0m).Should().BeApproximately(875m * 0.038m, 0.005m);
    }

    // ── DepositPrincipalRemaining: deposit-phase analog of PrincipalRemaining ──
    //
    // Mirrors the PrincipalRemaining shape but scoped to the deposit obligation.
    // Used by the team-registration "Deposit Due" display column. The bug it
    // replaced: the prior ad-hoc formula compared structural deposit against
    // gross PaidTotal, so a team that paid a DISCOUNTED deposit (e.g. $500
    // principal × 1.038 = $519 gross) showed Deposit Due $81 instead of $0.

    [Fact(DisplayName = "DepositRemaining: discount applied at deposit time — paid discounted deposit clears it")]
    public void DepositRemaining_DiscountedDepositFullyPaid_Zero()
    {
        // Deposit $600 − $100 discount = $500 effective; CC gross $519 → $500 principal.
        var s = State(cc: 519m, ccRate: 0.038m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 100m, lateFee: 0m, donation: 0m)
            .Should().BeApproximately(0m, 0.005m);
    }

    [Fact(DisplayName = "DepositRemaining: full deposit paid via CC (no discount) — clears via PrincipalPaid, not gross PaidTotal")]
    public void DepositRemaining_FullCcDeposit_Zero()
    {
        // Deposit $600 fully paid: CC gross $622.80 = $600 principal + $22.80 proc.
        // Old formula `paidTotal >= deposit` worked here by luck; new formula clears via principal.
        var s = State(cc: 622.80m, ccRate: 0.038m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().BeApproximately(0m, 0.005m);
    }

    [Fact(DisplayName = "DepositRemaining: nothing paid, no modifiers — owes full deposit")]
    public void DepositRemaining_NoPaymentNoModifiers_FullDeposit()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m).Should().Be(600m);
    }

    [Fact(DisplayName = "DepositRemaining: late fee added before payment — owes inflated deposit")]
    public void DepositRemaining_LateFeeNoPayment_InflatedDeposit()
    {
        // Symmetric to the discount bug: previously late fee was ignored, so a team
        // that registered late would under-display Deposit Due as just the structural $600.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 0m, lateFee: 50m, donation: 0m).Should().Be(650m);
    }

    [Fact(DisplayName = "DepositRemaining: late-fee-inclusive deposit paid — clears to 0")]
    public void DepositRemaining_LateFeeInclusiveDepositPaid_Zero()
    {
        // Deposit $600 + $50 late = $650; CC gross $674.70 = $650 principal + $24.70 proc.
        var s = State(cc: 674.70m, ccRate: 0.038m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 0m, lateFee: 50m, donation: 0m)
            .Should().BeApproximately(0m, 0.005m);
    }

    [Fact(DisplayName = "DepositRemaining: discount + late fee combine like PrincipalRemaining")]
    public void DepositRemaining_DiscountAndLateFee_Combine()
    {
        // Effective = 600 − 100 + 50 = 550; CC gross paid $570.90 = $549.999... principal
        // (within decimal precision of $550).
        var s = State(cc: 570.90m, ccRate: 0.038m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 100m, lateFee: 50m, donation: 0m)
            .Should().BeApproximately(0m, 0.005m);
    }

    [Fact(DisplayName = "DepositRemaining: discount > deposit — effective deposit clamps at 0")]
    public void DepositRemaining_DiscountExceedsDeposit_Zero()
    {
        // Big discount swallows the deposit entirely; remainder floats to balance via OwedTotal.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 800m, lateFee: 0m, donation: 0m).Should().Be(0m);
    }

    [Fact(DisplayName = "DepositRemaining: partial pay — owes the gap")]
    public void DepositRemaining_PartialCheckPay_OwesGap()
    {
        // Deposit $600 − $100 discount = $500 effective; paid $300 by check.
        var s = State(check: 300m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 100m, lateFee: 0m, donation: 0m).Should().Be(200m);
    }

    [Fact(DisplayName = "DepositRemaining: proc disabled — gross = principal, exact-pay clears")]
    public void DepositRemaining_ProcDisabled_GrossEqualsPrincipal()
    {
        var s = State(cc: 500m, bAdd: false);
        s.DepositPrincipalRemaining(deposit: 500m, discount: 0m, lateFee: 0m, donation: 0m).Should().Be(0m);
    }

    [Fact(DisplayName = "DepositRemaining: deposit overpaid (balance bleed) — clamps at 0")]
    public void DepositRemaining_Overpaid_ClampsAtZero()
    {
        // Team paid into balance phase; deposit obligation long satisfied.
        var s = State(check: 1500m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m).Should().Be(0m);
    }

    // ── DepositPrincipalRemainingProportional: TEAM "Deposit Due" (matches ArbTrialFeeSplitter) ──
    //
    // The team installment splits a discount PROPORTIONALLY across deposit + balance, so the
    // deposit-due display must show the deposit's pro-rata share of the net bill — not the
    // front-loaded value. FeeBase = Deposit + BalanceDue for a team.

    [Fact(DisplayName = "DepositProportional: no discount — owes the full structural deposit")]
    public void DepositProportional_NoDiscount_FullDeposit()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        // round(1000 × 200/1000) = 200
        s.DepositPrincipalRemainingProportional(feeBase: 1000m, deposit: 200m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().Be(200m);
    }

    [Fact(DisplayName = "DepositProportional: 10% discount — deposit drops by its SHARE, not front-loaded")]
    public void DepositProportional_Discount_SharedNotFrontLoaded()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        // net = 1000 − 100 = 900; round(900 × 200/1000) = 180 (NOT the front-load value of 100).
        s.DepositPrincipalRemainingProportional(feeBase: 1000m, deposit: 200m, discount: 100m, lateFee: 0m, donation: 0m)
            .Should().Be(180m);
    }

    [Fact(DisplayName = "DepositProportional: discount exceeds deposit — both legs stay > 0 (no ARB break)")]
    public void DepositProportional_DiscountExceedsDeposit_DepositStaysPositive()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        // net = 1000 − 300 = 700; round(700 × 200/1000) = 140. Front-load would zero the deposit and
        // break the ARB-Trial; proportional keeps it positive.
        s.DepositPrincipalRemainingProportional(feeBase: 1000m, deposit: 200m, discount: 300m, lateFee: 0m, donation: 0m)
            .Should().Be(140m);
    }

    [Fact(DisplayName = "DepositProportional: late fee shares pro-rata onto the deposit")]
    public void DepositProportional_LateFee_Shared()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        // net = 1000 + 50 = 1050; round(1050 × 200/1000) = 210.
        s.DepositPrincipalRemainingProportional(feeBase: 1000m, deposit: 200m, discount: 0m, lateFee: 50m, donation: 0m)
            .Should().Be(210m);
    }

    [Fact(DisplayName = "DepositProportional: player phase (FeeBase == deposit) equals the front-load helper")]
    public void DepositProportional_PlayerFeeBaseEqualsDeposit_MatchesFrontLoad()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        // A deposit-phase player stamps FeeBase = deposit, so the ratio is 1 and proportional ≡ front-load.
        var proportional = s.DepositPrincipalRemainingProportional(feeBase: 500m, deposit: 500m, discount: 100m, lateFee: 0m, donation: 0m);
        var frontLoad = s.DepositPrincipalRemaining(deposit: 500m, discount: 100m, lateFee: 0m, donation: 0m);
        proportional.Should().Be(frontLoad);
        proportional.Should().Be(400m);
    }

    [Fact(DisplayName = "DepositProportional: zero FeeBase — returns 0 (no divide-by-zero)")]
    public void DepositProportional_ZeroFeeBase_Zero()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.DepositPrincipalRemainingProportional(feeBase: 0m, deposit: 0m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().Be(0m);
    }

    // ── BalancePrincipalRemaining: full-payment-phase "Balance Due" display column ──
    //
    // Balance-phase analog of DepositPrincipalRemaining (total principal remaining
    // minus the deposit-phase remainder). The bug it replaced: the prior display
    // returned the immutable structural balance and ignored every payment but a
    // full-pay, so a team that received a director check/correction while the rep
    // was away showed "Balance Due" identical to a team that had paid nothing —
    // even though CC/Check Owed (via ResolveOwed) reflected the payment correctly.
    // FeeBase = Deposit + BalanceDue throughout (full-payment phase).

    [Fact(DisplayName = "BalanceRemaining: nothing paid — owes the full structural balance")]
    public void BalanceRemaining_NoPayment_FullBalance()
    {
        // Deposit $600, balance $2000, FeeBase $2600. Deposit unpaid → balance untouched.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.BalancePrincipalRemaining(feeBase: 2600m, deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().Be(2000m);
    }

    [Fact(DisplayName = "BalanceRemaining: deposit paid, balance untouched — still owes full balance")]
    public void BalanceRemaining_DepositOnlyPaid_FullBalance()
    {
        // $600 deposit paid by check; deposit obligation cleared, balance unchanged.
        var s = State(check: 600m);
        s.BalancePrincipalRemaining(feeBase: 2600m, deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().Be(2000m);
    }

    [Fact(DisplayName = "BalanceRemaining: director check covers deposit + part of balance — nets out the payment")]
    public void BalanceRemaining_CheckBleedsIntoBalance_NetsPayment()
    {
        // The reported scenario: no deposit paid, then a $1,000 director check applied.
        // Deposit $600 satisfied first; remaining $400 reduces the $2,000 balance → $1,600.
        // Old display returned the structural $2,000 (identical to an unpaid team).
        var s = State(check: 1000m);
        s.BalancePrincipalRemaining(feeBase: 2600m, deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().Be(1600m);
    }

    [Fact(DisplayName = "BalanceRemaining: fully paid — clamps at 0")]
    public void BalanceRemaining_FullyPaid_Zero()
    {
        var s = State(check: 2600m);
        s.BalancePrincipalRemaining(feeBase: 2600m, deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().Be(0m);
    }

    [Fact(DisplayName = "BalanceRemaining: discount absorbed by deposit — balance unaffected until deposit clears")]
    public void BalanceRemaining_DiscountWithinDeposit_BalanceUnchanged()
    {
        // $100 discount reduces the deposit obligation; the balance is still the full $2000
        // while the (discounted) deposit is unpaid. Mirrors PrincipalRemaining/DepositRemaining
        // both carrying the discount, which cancels in the difference.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.BalancePrincipalRemaining(feeBase: 2600m, deposit: 600m, discount: 100m, lateFee: 0m, donation: 0m)
            .Should().Be(2000m);
    }

    [Fact(DisplayName = "BalanceRemaining: CC payment nets out at principal, not gross")]
    public void BalanceRemaining_CcPayment_NetsPrincipalNotGross()
    {
        // CC gross $1038 → $1000 principal at 3.8%. Deposit $600 cleared, $400 into balance.
        var s = State(cc: 1038m, ccRate: 0.038m);
        s.BalancePrincipalRemaining(feeBase: 2600m, deposit: 600m, discount: 0m, lateFee: 0m, donation: 0m)
            .Should().BeApproximately(1600m, 0.01m);
    }

    // ── ResolveOwed: the single per-method owed resolver ──

    [Fact(DisplayName = "ResolveOwed: pristine team — CC full, check drops CC proc, eCheck drops only the rate difference")]
    public void ResolveOwed_NoPayments_DropsCcProcForCheck()
    {
        // base 500, embedded proc 19 (500 × 3.8%), owed 519, nothing paid.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var owed = s.ResolveOwed(owedTotal: 519m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 19m);

        owed.Cc.Should().BeApproximately(519m, 0.005m);     // full CC-inclusive owed
        owed.Check.Should().BeApproximately(500m, 0.005m);  // proc removed → principal only
        owed.Echeck.Should().BeApproximately(505m, 0.005m); // base + eCheck proc (500 × 1%)
    }

    [Fact(DisplayName = "ResolveOwed: proc disabled — every method equals the raw owed")]
    public void ResolveOwed_ProcDisabled_AllMethodsEqualOwed()
    {
        var s = PaymentState.Empty(false, 0.038m, 0.01m);
        var owed = s.ResolveOwed(owedTotal: 500m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 0m);

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
        var owed = s.ResolveOwed(owedTotal: 1712.70m, feeBase: 2100m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 79.80m);

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
        var owed = s.ResolveOwed(owedTotal: 529m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 10m, feeProcessing: 19m);

        owed.Check.Should().BeApproximately(510m, 0.005m); // 500 base + 10 donation, proc removed
    }

    [Fact(DisplayName = "ResolveOwed: nothing owed clamps every method at 0")]
    public void ResolveOwed_NothingOwed_ClampsAtZero()
    {
        var s = State(check: 600m);
        var owed = s.ResolveOwed(owedTotal: 0m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 0m);

        owed.Cc.Should().Be(0m);
        owed.Check.Should().Be(0m);
        owed.Echeck.Should().Be(0m);
    }

    // ── ProcCreditForCharge: partial-pay-aware per-charge credit ─────────────
    //
    // Distinct from ResolveOwed because the registration deposit flow charges a
    // fraction of the owed amount; the eCheck credit must clamp to what's actually
    // embedded in THIS charge, not the entity's full embedded proc.

    [Fact(DisplayName = "ProcCreditForCharge: full pay matches OwedTotal − ResolveOwed.Echeck")]
    public void ProcCreditForCharge_FullPay_MatchesResolveOwedEcheck()
    {
        // OwedTotal = 519 (500 base + 19 proc); eCheck credit at (3.8 − 1.0)% = $14.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var owed = s.ResolveOwed(owedTotal: 519m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 19m);

        var credit = s.ProcCreditForCharge(
            ccCharge: 519m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 19m, methodRate: 0.01m);

        credit.Should().BeApproximately(519m - owed.Echeck, 0.005m);
    }

    [Fact(DisplayName = "ProcCreditForCharge: deposit BELOW principal → zero credit (no proc embedded)")]
    public void ProcCreditForCharge_DepositBelowPrincipal_ZeroCredit()
    {
        // Base 500, full owed 519; a $100 deposit is principal-only. No CC proc to credit.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var credit = s.ProcCreditForCharge(
            ccCharge: 100m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 19m, methodRate: 0.01m);

        credit.Should().Be(0m);
    }

    [Fact(DisplayName = "ProcCreditForCharge: deposit ABOVE principal → credit clamped at embedded proc in charge")]
    public void ProcCreditForCharge_DepositAbovePrincipal_CappedAtChargeEmbeddedProc()
    {
        // Base 500, principal-remaining 500; charge 505 carries $5 of proc.
        // Raw rate-delta credit = 500 × 0.028 = $14. Capped at $5 = the proc in this charge.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var credit = s.ProcCreditForCharge(
            ccCharge: 505m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 19m, methodRate: 0.01m);

        credit.Should().Be(5m);
    }

    [Fact(DisplayName = "ProcCreditForCharge: proc disabled → zero credit regardless of inputs")]
    public void ProcCreditForCharge_ProcDisabled_Zero()
    {
        var s = PaymentState.Empty(bAddProcessingFees: false, ccRate: 0.038m, echeckRate: 0.01m);
        var credit = s.ProcCreditForCharge(
            ccCharge: 519m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 19m, methodRate: 0.01m);

        credit.Should().Be(0m);
    }

    [Fact(DisplayName = "ProcCreditForCharge: methodRate == CcRate (CC) → zero credit")]
    public void ProcCreditForCharge_CcMethod_Zero()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var credit = s.ProcCreditForCharge(
            ccCharge: 519m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 19m, methodRate: 0.038m);

        credit.Should().Be(0m);
    }

    [Fact(DisplayName = "ProcCreditForCharge: caps at FeeProcessing (over-credit guard)")]
    public void ProcCreditForCharge_CappedAtFeeProcessing()
    {
        // Raw rate-delta would credit $14, but the reg has only $3 of proc left.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var credit = s.ProcCreditForCharge(
            ccCharge: 519m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 0m, feeProcessing: 3m, methodRate: 0.01m);

        credit.Should().Be(3m);
    }

    // ── Donation threading: a donation is a late-fee-shaped, proc-bearing modifier ──

    [Fact(DisplayName = "FeeProcessingTarget: donation adds to the proc-bearing base")]
    public void Target_Donation_LeviesProcOnDonation()
    {
        // base 1000 + donation 100 = 1100 proc-bearing principal; nothing paid.
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.FeeProcessingTarget(1000m, 0m, 0m, donation: 100m)
            .Should().BeApproximately(1100m * 0.038m, 0.005m); // 41.80
    }

    [Fact(DisplayName = "DepositRemaining: donation adds to the deposit obligation like a late fee")]
    public void DepositRemaining_Donation_AddsToDepositDue()
    {
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        s.DepositPrincipalRemaining(deposit: 600m, discount: 0m, lateFee: 0m, donation: 25m)
            .Should().Be(625m);
    }

    [Fact(DisplayName = "ResolveOwed: donation is proc-bearing — eCheck owes its principal plus eCheck proc")]
    public void ResolveOwed_Donation_EcheckIncludesDonation()
    {
        // base 500 + donation 100 -> proc-bearing 600. CC owed 622.80 (600 x 1.038);
        // check owed 600 (CC proc removed); eCheck owed 606 (600 x 1.01).
        var s = PaymentState.Empty(true, 0.038m, 0.01m);
        var owed = s.ResolveOwed(owedTotal: 622.80m, feeBase: 500m, discount: 0m, lateFee: 0m, donation: 100m, feeProcessing: 22.80m);

        owed.Cc.Should().BeApproximately(622.80m, 0.005m);
        owed.Check.Should().BeApproximately(600m, 0.005m);
        owed.Echeck.Should().BeApproximately(606m, 0.005m);
    }
}
