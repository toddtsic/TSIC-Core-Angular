using TSIC.Domain.Constants;

namespace TSIC.Contracts.Payments;

/// <summary>
/// Pure rate-clamping for the configured CC and eCheck processing rates.
/// Mirrors the legacy clamps (CC: 3.5–4.0%, eCheck: 1.5–2.0%) and converts
/// the percent-form input into a ready-to-multiply decimal (e.g. 0.038).
/// </summary>
public static class ProcessingRateMath
{
    /// <summary>Clamp the raw CC processing percent and convert to decimal multiplier.</summary>
    public static decimal ToCcMultiplier(decimal? rawPercent)
    {
        var raw = rawPercent ?? FeeConstants.MinProcessingFeePercent;
        return System.Math.Clamp(raw, FeeConstants.MinProcessingFeePercent, FeeConstants.MaxProcessingFeePercent) / 100m;
    }

    /// <summary>Clamp the raw eCheck processing percent and convert to decimal multiplier.</summary>
    public static decimal ToEcheckMultiplier(decimal? rawPercent)
    {
        var raw = rawPercent ?? FeeConstants.MinEcprocessingFeePercent;
        return System.Math.Clamp(raw, FeeConstants.MinEcprocessingFeePercent, FeeConstants.MaxEcprocessingFeePercent) / 100m;
    }
}
