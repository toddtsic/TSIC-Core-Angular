namespace TSIC.API.Services.Shared.TextSubstitution;

public interface ITextSubstitutionService
{
    /// <summary>
    /// Substitutes tokens in a template. Caller-supplied <paramref name="extraTokens"/>
    /// are merged into the engine's token dictionary (overriding any collisions) so
    /// callers with out-of-band data (e.g. USLax per-recipient ping results) do not
    /// need their own Replace chain. Ordering is handled centrally by TokenReplacer,
    /// which sorts by descending key length before replacing.
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
    /// Substitutes job-level tokens only (e.g., !JOBNAME, !USLAXVALIDTHROUGHDATE).
    /// Use this for anonymous/public content like bulletins and menus.
    /// </summary>
    Task<string> SubstituteJobTokensAsync(string jobPath, string template);
}
