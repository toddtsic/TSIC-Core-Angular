namespace TSIC.Application.Services.Players;

/// <summary>
/// Pure business logic for player registration fee calculation.
/// No framework dependencies (no IConfiguration, no EF Core, no ASP.NET).
/// </summary>
public sealed class PlayerFeeCalculator : IPlayerFeeCalculator
{
    private readonly decimal _creditCardPercentage;

    public PlayerFeeCalculator(decimal creditCardPercentage)
    {
        _creditCardPercentage = creditCardPercentage;
    }

    public (decimal ProcessingFee, decimal FeeTotal) ComputeTotals(
        decimal baseFee,
        decimal discount,
        decimal donation,
        decimal? processingFeeOverride = null)
    {
        var processingFee = processingFeeOverride ?? (baseFee * _creditCardPercentage);
        var total = baseFee + processingFee - discount - donation;
        return (processingFee, total);
    }
}

