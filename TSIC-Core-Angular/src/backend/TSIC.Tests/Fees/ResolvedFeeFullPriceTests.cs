using FluentAssertions;
using TSIC.Contracts.Repositories;

namespace TSIC.Tests.Fees;

/// <summary>
/// Pins <see cref="ResolvedFee.FullPrice"/> — THE single formula every full-payment path
/// (new-reg full phase, PIF upgrade, player/team swap, pool transfer preview, registered-grid
/// "paid in full?" check) now uses for "deposit slice + balance slice".
///
/// Regression guard for the silent double-charge: a no-deposit fee is configured as
/// Deposit=NULL / BalanceDue=X. EffectiveDeposit falls back to BalanceDue, so the old
/// per-site sum EffectiveDeposit + EffectiveBalanceDue resolved a $325 fee to $650 and the
/// Pay-In-Full recompute billed the player twice (caught only by the promise guard). FullPrice
/// treats a NULL deposit as $0, so the full price is the balance alone.
/// </summary>
public class ResolvedFeeFullPriceTests
{
    [Fact(DisplayName = "FullPrice: NULL deposit is NOT double-counted (the Brynn double-charge)")]
    public void NullDeposit_DoesNotDouble()
    {
        var resolved = new ResolvedFee { FeeConfigured = true, Deposit = null, BalanceDue = 325m };

        // EffectiveDeposit falls back to BalanceDue by design — proving why summing it doubles.
        resolved.EffectiveDeposit.Should().Be(325m);
        resolved.FullPrice.Should().Be(325m); // NOT 650m
    }

    [Fact(DisplayName = "FullPrice: genuine deposit + balance split sums to the whole fee")]
    public void RealSplit_SumsBothSlices()
    {
        var resolved = new ResolvedFee { FeeConfigured = true, Deposit = 100m, BalanceDue = 225m };

        resolved.FullPrice.Should().Be(325m);
    }

    [Fact(DisplayName = "FullPrice: deposit-only config (NULL balance) is the deposit alone")]
    public void DepositOnly_NullBalance()
    {
        var resolved = new ResolvedFee { FeeConfigured = true, Deposit = 325m, BalanceDue = null };

        resolved.FullPrice.Should().Be(325m);
    }

    [Fact(DisplayName = "FullPrice: both NULL resolves to zero (free / unconfigured)")]
    public void BothNull_IsZero()
    {
        var resolved = new ResolvedFee { FeeConfigured = true, Deposit = null, BalanceDue = null };

        resolved.FullPrice.Should().Be(0m);
    }
}
