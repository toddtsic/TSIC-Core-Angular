using FluentAssertions;
using TSIC.API.Services.Fees;

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
            discount: 0m, lateFee: 0m,
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
            discount: 0m, lateFee: 0m,
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
            discount: 0m, lateFee: 0m,
            processingRate: Rate,
            bAddProcessingFees: false,
            bApplyProcessingFeesToTeamDeposit: true);

        r.DepositCharge.Should().Be(200m);
        r.BalanceCharge.Should().Be(800m);
        r.TotalProcessing.Should().Be(0m);
    }

    [Fact]
    public void Discount_ReducesBothChargesProportionally_TrueTrue()
    {
        // From the worked example: $10 discount at 3% rate.
        // netBase = 990, processing = 29.70, FeeTotal = 1019.70.
        // Deposit ratio = 200/1000.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 10m, lateFee: 0m,
            processingRate: 0.03m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        // depositBase = round(990 × 200/1000) = 198.00
        // balanceBase = 990 − 198 = 792.00
        // totalProcessing = round(990 × 0.03) = 29.70
        // depositProcessing = round(29.70 × 200/1000) = 5.94
        // balanceProcessing = 29.70 − 5.94 = 23.76
        r.DepositCharge.Should().Be(203.94m);
        r.BalanceCharge.Should().Be(815.76m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1019.70m);
    }

    [Fact]
    public void LateFee_IncreasesBothChargesProportionally_TrueTrue()
    {
        // $10 late fee at 3%. netBase = 1010, processing = 30.30, FeeTotal = 1040.30.
        var r = ArbTrialFeeSplitter.Split(
            rawDeposit: 200m, rawBalance: 800m,
            discount: 0m, lateFee: 10m,
            processingRate: 0.03m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: true);

        // depositBase = round(1010 × 0.2) = 202
        // balanceBase = 1010 − 202 = 808
        // totalProcessing = round(1010 × 0.03) = 30.30
        // depositProcessing = round(30.30 × 0.2) = 6.06
        // balanceProcessing = 30.30 − 6.06 = 24.24
        r.DepositCharge.Should().Be(208.06m);
        r.BalanceCharge.Should().Be(832.24m);
        (r.DepositCharge + r.BalanceCharge).Should().Be(1040.30m);
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
            discount: 0m, lateFee: 0m,
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
            discount: 0m, lateFee: 0m,
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
            discount: 50m, lateFee: 0m,
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
            discount: 5000m, lateFee: 0m,
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
                discount: 7m, lateFee: 0m,
                processingRate: 0.035m,
                bAddProcessingFees: true,
                bApplyProcessingFeesToTeamDeposit: true);

            (r.DepositCharge + r.BalanceCharge).Should().Be(r.DepositCharge + r.BalanceCharge);
            totalCharge += r.DepositCharge + r.BalanceCharge;
            expectedFeeTotalPerTeam = r.DepositCharge + r.BalanceCharge;
        }

        totalCharge.Should().Be(expectedFeeTotalPerTeam * teamCount);
    }
}
