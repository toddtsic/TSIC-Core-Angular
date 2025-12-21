namespace TSIC.Application.Services.Players;

/// <summary>
/// Interface for player registration fee calculation.
/// Keeps fee calculation logic testable and framework-independent.
/// </summary>
public interface IPlayerFeeCalculator
{
    /// <summary>
    /// Computes registration totals: processing fee and final total.
    /// Business rules:
    ///  - Processing fee = baseFee * ccPercentage (unless overridden)
    ///  - Total = baseFee + processingFee - discount - donation
    /// </summary>
    (decimal ProcessingFee, decimal FeeTotal) ComputeTotals(
        decimal baseFee,
        decimal discount,
        decimal donation,
        decimal? processingFeeOverride = null);
}

