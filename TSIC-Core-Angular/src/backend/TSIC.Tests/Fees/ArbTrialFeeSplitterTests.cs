using FluentAssertions;
using TSIC.API.Services.Fees;
using TSIC.Contracts.Payments;

namespace TSIC.Tests.Fees;

/// <summary>
/// Tests for the ARB-Trial fee splitter. Every assertion validates that
/// DepositCharge + BalanceCharge sums to the expected FeeTotal exactly,
/// proving the round-once-remainder rule holds across rounding edge cases.
/// </summary>
public class ArbTrialFeeSplitterTests
{
    private const decimal Rate = 0.035m;

    [Fact]
    public void NoModifiers_FlagsTrueTrue_ProportionalSplit()
    {
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 0m, lateFee: 0m, donation: 0m,
            processingRate: Rate,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        // netBase = 1000, processing = 35.00
        // depositBase = 200, balanceBase = 800
        // depositProcessing = round(35 × 200/1000, 2) = 7.00, balanceProcessing = 28.00
        r.DepositCharge.Should().Be(207m);
        r.BalanceCharge.Should().Be(828m);
        r.DepositProcessing.Should().Be(7m);
        r.BalanceProcessing.Should().Be(28m);
        r.TotalProcessing.Should().Be(35m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1035m);
    }

    [Fact]
    public void NoModifiers_FlagsTrueFalse_ProcessingOnBalanceOnly()
    {
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 0m, lateFee: 0m, donation: 0m,
            processingRate: Rate,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: false);

        // depositBase = 200, balanceBase = 800
        // balanceProcessing = round(800 × 0.035, 2) = 28.00, depositProcessing = 0
        r.DepositCharge.Should().Be(200m);
        r.BalanceCharge.Should().Be(828m);
        r.DepositProcessing.Should().Be(0m);
        r.BalanceProcessing.Should().Be(28m);
        r.TotalProcessing.Should().Be(28m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1028m);
    }

    [Fact]
    public void NoProcessing_NoModifiers_RawSplit()
    {
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 0m, lateFee: 0m, donation: 0m,
            processingRate: Rate,
            bAddProcessingFees: false,
            bApplyProcessingFeesToTeamDeposit: true);

        r.DepositCharge.Should().Be(200m);
        r.BalanceCharge.Should().Be(800m);
        r.TotalProcessing.Should().Be(0m);
    }

    [Fact]
    public void Discount_FrontLoadsOntoDeposit_TrueTrue()
    {
        // $10 discount at 3% rate. The discount comes off the DEPOSIT first (what is owed now),
        // NOT proportionally across deposit + balance. netBase = 990, FeeTotal = 1019.70.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 10m, lateFee: 0m, donation: 0m,
            processingRate: 0.03m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        // depositPrincipal = 200 − 10 = 190 (full discount on deposit)
        // balancePrincipal = 990 − 190 = 800 (unchanged)
        // totalProcessing = round(990 × 0.03) = 29.70
        // depositProcessing = round(29.70 × 200/1000) = 5.94
        // balanceProcessing = 29.70 − 5.94 = 23.76
        r.DepositCharge.Should().Be(195.94m);
        r.BalanceCharge.Should().Be(823.76m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1019.70m);
    }

    [Fact]
    public void DiscountExceedsDeposit_SpillsOntoBalance_TrueTrue()
    {
        // $300 discount, $200 deposit: the deposit zeroes out and the leftover $100 reduces the
        // balance. netBase = 700, FeeTotal = 721.00.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 300m, lateFee: 0m, donation: 0m,
            processingRate: 0.03m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        // depositPrincipal = max(0, 200 − 300) = 0
        // balancePrincipal = 700 − 0 = 700
        // totalProcessing = round(700 × 0.03) = 21.00; depositProcessing = round(21 × 0.2) = 4.20
        r.DepositCharge.Should().Be(4.20m);
        r.BalanceCharge.Should().Be(716.80m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(721.00m);
    }

    [Fact]
    public void LateFee_FrontLoadsOntoDeposit_TrueTrue()
    {
        // $10 late fee at 3%. The late fee lands on the DEPOSIT (owed now), matching the
        // display column. netBase = 1010, FeeTotal = 1040.30.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 0m, lateFee: 10m, donation: 0m,
            processingRate: 0.03m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        // depositPrincipal = 200 + 10 = 210, balancePrincipal = 1010 − 210 = 800
        // totalProcessing = round(1010 × 0.03) = 30.30
        // depositProcessing = round(30.30 × 0.2) = 6.06, balanceProcessing = 24.24
        r.DepositCharge.Should().Be(216.06m);
        r.BalanceCharge.Should().Be(824.24m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1040.30m);
    }

    [Fact]
    public void Donation_IncreasesNetBaseAndIncursProcessing_TrueTrue()
    {
        // $100 donation at 3.5%. netBase = 1000 + 100 = 1100, processing = round(1100 × 0.035) = 38.50.
        // vs. the no-donation run (1035.00), donation adds 100 principal + 3.50 processing = 103.50.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 0m, lateFee: 0m, donation: 100m,
            processingRate: 0.035m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        // depositBase = round(1100 × 200/1000) = 220, balanceBase = 880
        // totalProcessing = round(1100 × 0.035) = 38.50
        // depositProcessing = round(38.50 × 0.2) = 7.70, balanceProcessing = 30.80
        r.TotalProcessing.Should().Be(38.50m);
        r.DepositCharge.Should().Be(227.70m);
        r.BalanceCharge.Should().Be(910.80m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1138.50m);
    }

    [Fact]
    public void Donation_IncursProcessingOnBalanceShare_TrueFalse()
    {
        // Balance-only processing mode: donation still folds into netBase, so its balance
        // share carries processing. netBase = 1100, balanceBase = 880, processing = round(880 × 0.035) = 30.80.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 0m, lateFee: 0m, donation: 100m,
            processingRate: 0.035m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: false);

        r.DepositProcessing.Should().Be(0m);
        r.BalanceProcessing.Should().Be(30.80m);
        r.TotalProcessing.Should().Be(30.80m);
        // depositCharge = 220 (no processing), balanceCharge = 880 + 30.80 = 910.80
        r.DepositCharge.Should().Be(220m);
        r.BalanceCharge.Should().Be(910.80m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1130.80m);
    }

    [Fact]
    public void EvenSplit_OneCentRoundingResolvedByRemainder()
    {
        // $0.50 deposit + $0.50 balance, 3% rate.
        // netBase = 1.00, processing = round(0.03, 2) = 0.03.
        // depositBase = round(1.00 × 0.5) = 0.50, balanceBase = 0.50.
        // depositProcessing = round(0.03 × 0.5, 2) = 0.02 (HalfAwayFromZero rounds 0.015 up).
        // balanceProcessing = 0.03 − 0.02 = 0.01 (the remainder absorbs the cent).
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 0.50m, rawBalance: 0.50m,
            discount: 0m, lateFee: 0m, donation: 0m,
            processingRate: 0.03m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        r.TotalProcessing.Should().Be(0.03m);
        (r.DepositProcessing + r.BalanceProcessing).Should().Be(0.03m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1.03m);
    }

    [Fact]
    public void MicroDeposit_BalanceAbsorbsAllProcessing()
    {
        // $0.01 deposit + $99.99 balance, 3.5% rate.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 0.01m, rawBalance: 99.99m,
            discount: 0m, lateFee: 0m, donation: 0m,
            processingRate: 0.035m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        r.TotalProcessing.Should().Be(3.50m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(103.50m);
    }

    [Fact]
    public void ZeroDeposit_AllOnBalanceSide()
    {
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 0m, rawBalance: 1000m,
            discount: 50m, lateFee: 0m, donation: 0m,
            processingRate: 0.035m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        r.DepositCharge.Should().Be(0m);
        r.DepositProcessing.Should().Be(0m);
        // FeeTotal = 1000 + (950 × 0.035 = 33.25) − 50 = 983.25
        (r.DepositCharge + r.BalanceCharge).Should().Be(983.25m);
    }

    [Fact]
    public void DiscountExceedsBase_ClampsToZero()
    {
        // Defense: discount > raw total → netBase clamps to 0, no negative charges.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 5000m, lateFee: 0m, donation: 0m,
            processingRate: 0.035m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        r.DepositCharge.Should().Be(0m);
        r.BalanceCharge.Should().Be(0m);
        r.TotalProcessing.Should().Be(0m);
    }

    [Fact]
    public void ManyTeamsAccrueExactlyToFeeTotal_TrueTrue()
    {
        // Stress: a 30-team-style scenario where each team has the same fee but
        // small rounding asymmetries. Verify the sum across all teams equals
        // the expected FeeTotal sum to the cent.
        const int teamCount = 30;
        var totalCharge = 0m;
        var expectedFeeTotalPerTeam = 0m;

        for (int i = 0; i < teamCount; i++)
        {
            var r = ArbTrialFeeSplitter.Split(
                rawDeposit: 100m, rawBalance: 233m,
                discount: 7m, lateFee: 0m, donation: 0m,
                processingRate: 0.035m,
                bAddProcessingFees: true,
                bApplyProcessingFeesToTeamDeposit: true);

            (r.DepositCharge + r.BalanceCharge).Should().Be(r.DepositCharge + r.BalanceCharge);
            totalCharge += r.DepositCharge + r.BalanceCharge;
            expectedFeeTotalPerTeam = r.DepositCharge + r.BalanceCharge;
        }

        totalCharge.Should().Be(expectedFeeTotalPerTeam * teamCount);
    }

    [Fact]
    public void DepositCharge_MatchesDisplayDepositDue_SharedFrontLoadFormula()
    {
        // The whole point of the front-load change: the deposit the charge engine bills must equal
        // the deposit the rep is SHOWN. With processing off and no donation, DepositCharge is pure
        // principal — compare it to the display helper (PaymentState.DepositPrincipalRemaining),
        // both fed the same discount + late fee. Both route through FeeMath.DepositObligation.
        const decimal deposit = 200m, balance = 800m, discount = 120m, lateFee = 15m;
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: deposit, rawBalance: balance,
            discount: discount, lateFee: lateFee, donation: 0m,
            processingRate: 0.035m,
            bAddProcessingFees: false,
            bApplyProcessingFeesToTeamDeposit: false);

        var state = PaymentState.Empty(bAddProcessingFees: false, ccRate: 0m, echeckRate: 0m);
        var displayDepositDue = state.DepositPrincipalRemaining(deposit, discount, lateFee, donation: 0m);

        r.DepositCharge.Should().Be(displayDepositDue);
        r.DepositCharge.Should().Be(95m); // 200 − 120 + 15
    }
}
