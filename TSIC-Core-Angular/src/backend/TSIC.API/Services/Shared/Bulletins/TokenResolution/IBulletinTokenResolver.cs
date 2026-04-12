namespace TSIC.API.Services.Shared.Bulletins.TokenResolution;

/// <summary>
/// Resolves one bulletin-substitution token (e.g. REGISTER_PLAYER) into final HTML.
/// Registered in DI; discovered by BulletinTokenRegistry via TokenName.
/// </summary>
public interface IBulletinTokenResolver
{
    /// <summary>
    /// Token name without braces (e.g. "REGISTER_PLAYER" for {{REGISTER_PLAYER}}).
    /// Must be unique across all registered resolvers.
    /// </summary>
    string TokenName { get; }

    /// <summary>
    /// Emit final HTML for this token under the given context.
    /// Return empty string to hide the token (e.g. when gating pulse flags are false).
    /// </summary>
    string Resolve(TokenContext ctx);
}
