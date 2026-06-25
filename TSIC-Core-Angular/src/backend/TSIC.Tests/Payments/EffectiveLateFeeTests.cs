using FluentAssertions;
using TSIC.Contracts.Payments;
using Xunit;

namespace TSIC.Tests.Payments;

/// <summary>
/// The derived "pay late ⇒ owe more" late-fee model lives in PaymentState.EffectiveLateFee.
/// A late fee is no longer a stamp frozen at signup; it is minted ONCE at the qualifying payment
/// then locked. Rule: once a late fee has been COLLECTED, the record is closed to further late fee —
/// so the result is the late fee already PAID (capped at the configured amount) if any was collected,
/// otherwise the active windowed modifier while base principal is still owed. (The collected fee and
/// the gate are mutually exclusive — base-first allocation means a fee can only be collected once the
/// base is paid, at which point the PIF-exempt gate is already 0 — so this never climbs a paid fee.)
/// These tests pin every branch — the wiring that feeds this into recompute, the payment charge, and
/// the owed displays must not change these answers.
/// </summary>
public class EffectiveLateFeeTests
{
    // FeeBase (full price) = 500, no discount/donation unless noted. Late fee = 30.
    // depositBase defaults to Full for the single-phase/PIF cases below (no separate deposit tier →
    // EffectiveDeposit == FullPrice); the deposit-phase regressions pass a distinct Deposit.
    private const decimal Full = 500m;
    private const decimal Deposit = 100m;
    private const decimal Late = 30m;

    private static PaymentState Paid(decimal check = 0m, decimal cc = 0m, decimal ccRate = 0.038m) =>
        new()
        {
            CcGrossPaid = cc, EcheckGrossPaid = 0m, CheckPaid = check, CashPaid = 0m, CorrectionApplied = 0m,
            BAddProcessingFees = true, CcRate = ccRate, EcheckRate = 0.01m,
        };

    [Fact(DisplayName = "New reg, in window, owes everything → full late fee applies")]
    public void NewReg_InWindow_Applies()
    {
        Paid().EffectiveLateFee(windowedLateFee: Late, configuredLateFee: Late, Full, Full, 0m, 0m)
            .Should().Be(Late);
    }

    [Fact(DisplayName = "Out of window, still owing, nothing paid → no late fee")]
    public void OutOfWindow_Owing_Zero()
    {
        // windowedLateFee = 0 (modifier not active now); configured stays 30 but no paid floor.
        Paid().EffectiveLateFee(windowedLateFee: 0m, configuredLateFee: Late, Full, Full, 0m, 0m)
            .Should().Be(0m);
    }

    [Fact(DisplayName = "PIF exempt: paid the full base in-window → no late fee (gate excludes the late fee)")]
    public void PaidBaseOnly_InWindow_Exempt()
    {
        // PIF-exempt rule: a record that has paid the full base is never assessed a late fee.
        // The gate measures remaining base EXCLUDING the late fee, so paying base to 0 drops it.
        // (Lenient side of "PIF is exempt": paying exactly the base while in-window also exempts.)
        Paid(check: Full).EffectiveLateFee(windowedLateFee: Late, configuredLateFee: Late, Full, Full, 0m, 0m)
            .Should().Be(0m);
    }

    [Fact(DisplayName = "Lock survives the window closing: paid base+late, window now closed → still 30")]
    public void PaidLate_WindowClosed_Locked()
    {
        // windowedLateFee = 0 (closed) but configured = 30 → paid floor holds the lock.
        Paid(check: Full + Late).EffectiveLateFee(windowedLateFee: 0m, configuredLateFee: Late, Full, Full, 0m, 0m)
            .Should().Be(Late);
    }

    [Fact(DisplayName = "Delete modifier after late fee paid → drops to 0 (overpayment → refund)")]
    public void DeleteAfterPaid_DropsToZero()
    {
        // configured = 0 (deleted) caps the floor at 0; the $30 they paid becomes negative owed downstream.
        Paid(check: Full + Late).EffectiveLateFee(windowedLateFee: 0m, configuredLateFee: 0m, Full, Full, 0m, 0m)
            .Should().Be(0m);
    }

    [Fact(DisplayName = "Edit late fee down (30→10) after paying 30 → effective 10 (refund the 20)")]
    public void EditDownAfterPaid_DropsToNewAmount()
    {
        Paid(check: Full + Late).EffectiveLateFee(windowedLateFee: 10m, configuredLateFee: 10m, Full, Full, 0m, 0m)
            .Should().Be(10m);
    }

    [Fact(DisplayName = "Paid base + partial late in-window → floor holds the 10 paid (base is PIF, gate 0)")]
    public void PartialLatePaid_InWindow_FloorWins()
    {
        // Base is fully paid → gate 0 (PIF-exempt). The 10 of late fee already collected locks via the
        // floor: collected paidFloor min(30, 510−500)=10 wins (gate is 0). Only collected dollars stick.
        Paid(check: Full + 10m).EffectiveLateFee(windowedLateFee: Late, configuredLateFee: Late, Full, Full, 0m, 0m)
            .Should().Be(10m);
    }

    [Fact(DisplayName = "Raise late fee after it was paid → stays at the collected amount (no climb)")]
    public void RaiseAfterPaid_DoesNotClimb()
    {
        // Paid base + 30. Director later raises the modifier to 50 with the window open. The rule
        // "once collected, the record is closed to further late fee" holds it at the collected 30 —
        // a paid late fee must never grow. (Mechanically the gate is already 0 here: the base is paid.)
        Paid(check: Full + Late).EffectiveLateFee(windowedLateFee: 50m, configuredLateFee: 50m, Full, Full, 0m, 0m)
            .Should().Be(Late);
    }

    [Fact(DisplayName = "No late fee configured → always 0, regardless of payments")]
    public void NoModifier_Zero()
    {
        Paid(check: Full).EffectiveLateFee(windowedLateFee: 0m, configuredLateFee: 0m, Full, Full, 0m, 0m)
            .Should().Be(0m);
    }

    [Fact(DisplayName = "Discount does not suppress an in-window late fee on a new reg")]
    public void WithDiscount_InWindow_Applies()
    {
        // Full 500, discount 50 → net base 450; still owes → late applies.
        Paid().EffectiveLateFee(windowedLateFee: Late, configuredLateFee: Late, Full, Full, discount: 50m, donation: 0m)
            .Should().Be(Late);
    }

    [Fact(DisplayName = "Lock survives with proc enabled: CC-paid base+late, window closed → 30")]
    public void Proc_PaidLate_WindowClosed_Locked()
    {
        // CC gross (500+30) × 1.038 = 550.14 → principal reverses to 530.
        Paid(cc: (Full + Late) * 1.038m).EffectiveLateFee(windowedLateFee: 0m, configuredLateFee: Late, Full, Full, 0m, 0m)
            .Should().BeApproximately(Late, 0.01m);
    }

    // ── Deposit-phase floor: the late fee is billed on the DEPOSIT tier, not the full price ──
    // Regression for the deposit→balance-due flip that silently refunded an already-paid late fee.
    // A deposit-phase reg pays deposit + late (here 100 + 30 = 130) and the late fee must lock against
    // the DEPOSIT (depositBase = 100), not the full price (500). Measuring the floor against fullPrice
    // reported 0 collected, so a flip-to-full re-derive with the window closed wiped the paid late fee.

    [Fact(DisplayName = "Deposit-paid late, flipped to full, window CLOSED → late fee stays locked (was wiped to 0)")]
    public void DepositPaidLate_FlippedToFull_WindowClosed_Locked()
    {
        // Paid deposit + late = 130. depositBase = 100, fullPrice = 500. Window closed (windowed 0).
        // Floor: min(30, max(0, 130 − 100)) = 30. With the old fullPrice base this was
        // min(30, max(0, 130 − 500)) = 0 and the gate (closed) was 0 → the paid $30 vanished.
        Paid(check: Deposit + Late).EffectiveLateFee(windowedLateFee: 0m, configuredLateFee: Late, Full, Deposit, 0m, 0m)
            .Should().Be(Late);
    }

    [Fact(DisplayName = "Deposit-paid late, flipped to full, window OPEN → holds the collected amount, not re-gated")]
    public void DepositPaidLate_FlippedToFull_WindowOpen_FloorWins()
    {
        // Window open (windowed 30) but the floor already locked the collected 30, so a later raise
        // can't climb it. Floor min(30, 130 − 100) = 30 wins over the gate.
        Paid(check: Deposit + Late).EffectiveLateFee(windowedLateFee: 50m, configuredLateFee: 50m, Full, Deposit, 0m, 0m)
            .Should().Be(Late);
    }

    [Fact(DisplayName = "Deposit tier paid WITHOUT the late portion, window closed → late falls off (straddle tradeoff)")]
    public void DepositPaidNoLate_WindowClosed_FallsOff()
    {
        // Paid exactly the deposit base (100), never the late portion; window closed. Nothing collected
        // beyond the deposit base → floor 0, gate 0. The unpaid late fee drops (the end-date is the
        // persistence control, per the derived-late-fee design).
        Paid(check: Deposit).EffectiveLateFee(windowedLateFee: 0m, configuredLateFee: Late, Full, Deposit, 0m, 0m)
            .Should().Be(0m);
    }
}
