namespace TSIC.API.Services.Shared.Bulletins.TokenResolution;

/// <summary>
/// Resolves one bulletin-substitution token (e.g. REGISTER_PLAYER) into final HTML.
/// Registered in DI; discovered by BulletinTokenRegistry via TokenName.
/// </summary>
public interface IBulletinTokenResolver
{
    /// <summary>
    /// Token name without the leading '!' (e.g. "REGISTER_PLAYER" for !REGISTER_PLAYER).
    /// Must be unique across all registered resolvers.
    /// </summary>
    string TokenName { get; }

    /// <summary>
    /// Short human-readable description shown in the author-facing catalog
    /// (e.g. "Player registration CTA").
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Names of JobPulseDto properties this resolver reads to decide visibility
    /// (e.g. ["PlayerRegistrationOpen"]). Empty array = always visible.
    /// Viewer identity is NOT a valid gate — bulletins are public-facing.
    /// </summary>
    string[] GatingConditions { get; }

    /// <summary>
    /// Emit final HTML for this token under the given context.
    /// Return empty string to hide the token (e.g. when gating pulse flags are false).
    /// </summary>
    string Resolve(TokenContext ctx);
}
