namespace TSIC.API.Services.Shared.TextSubstitution;

public interface ITextSubstitutionService
{
    /// <summary>
    /// Substitutes tokens in a template. Caller-supplied <paramref name="extraTokens"/>
    /// are merged into the engine's token dictionary for callers with out-of-band data
    /// (e.g. USLax per-recipient ping results) so they do not need their own Replace chain.
    /// The engine owns its token namespace: if an extra key collides with a token the
    /// engine already produces, <see cref="InvalidOperationException"/> is thrown — two
    /// sources of truth for the same token is always a bug. Ordering is handled centrally
    /// by TokenReplacer, which sorts by descending key length before replacing.
    /// </summary>
    Task<string> SubstituteAsync(
        string jobSegment,
        Guid jobId,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string familyUserId,
        string template,
        string? inviteTargetJobPath = null,
        IReadOnlyDictionary<string, string>? extraTokens = null);

    /// <summary>
    /// Single-pass render of BOTH subject and body for one recipient. Loads fixed fields ONCE and
    /// builds the token dictionary once (guarded against both templates), then applies it to each —
    /// halving the per-recipient DB work versus two <see cref="SubstituteAsync"/> calls (render-win #1).
    /// Returns the templates unchanged with NO data load when neither contains a token (render-win #0).
    /// </summary>
    Task<(string Subject, string Body)> SubstituteSubjectAndBodyAsync(
        string jobSegment,
        Guid jobId,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string familyUserId,
        string subjectTemplate,
        string bodyTemplate,
        string? inviteTargetJobPath = null,
        IReadOnlyDictionary<string, string>? extraTokens = null);

    /// <summary>
    /// Substitutes job-level tokens only (e.g., !JOBNAME, !USLAXVALIDTHROUGHDATE).
    /// Use this for anonymous/public content like bulletins and menus.
    /// </summary>
    Task<string> SubstituteJobTokensAsync(string jobPath, string template);
}
