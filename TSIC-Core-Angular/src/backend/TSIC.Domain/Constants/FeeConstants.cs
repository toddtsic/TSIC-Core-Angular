namespace TSIC.Domain.Constants;

public static class FeeConstants
{
    /// <summary>
    /// Minimum CC processing fee rate stored as percentage (3.5 = 3.5%).
    /// Jobs can only override upward. NULL column = grandfathered at this rate.
    /// </summary>
    public const decimal MinProcessingFeePercent = 3.5m;

    /// <summary>
    /// Maximum CC processing fee rate. Safety ceiling — guards against
    /// admin typos like 35% when 3.5% was intended. Runtime clamps and
    /// save validation both enforce this.
    /// </summary>
    public const decimal MaxProcessingFeePercent = 4.0m;

    /// <summary>
    /// Current CC processing rate applied to newly created and cloned jobs (3.8 = 3.8%).
    /// Distinct from <see cref="MinProcessingFeePercent"/> (3.5): existing jobs keep the
    /// rate they began at; only new/cloned jobs start at this current rate.
    /// </summary>
    public const decimal NewJobProcessingFeePercent = 3.8m;

    /// <summary>
    /// Minimum eCheck processing fee rate stored as percentage (1.5 = 1.5%).
    /// Jobs can only override upward.
    /// </summary>
    public const decimal MinEcprocessingFeePercent = 1.5m;

    /// <summary>
    /// Maximum eCheck processing fee rate. Safety ceiling.
    /// </summary>
    public const decimal MaxEcprocessingFeePercent = 2.0m;

    /// <summary>
    /// Current eCheck processing rate applied to newly created and cloned jobs (1.5 = 1.5%).
    /// eCheck counterpart of <see cref="NewJobProcessingFeePercent"/>.
    /// </summary>
    public const decimal NewJobEcprocessingFeePercent = 1.5m;

    // Modifier types stored in fees.FeeModifiers.ModifierType
    public const string ModifierEarlyBird = "EarlyBird";
    public const string ModifierLateFee = "LateFee";
}
