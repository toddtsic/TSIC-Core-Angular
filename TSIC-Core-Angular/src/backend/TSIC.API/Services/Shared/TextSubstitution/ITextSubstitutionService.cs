namespace TSIC.API.Services.Shared.TextSubstitution;

public interface ITextSubstitutionService
{
    Task<string> SubstituteAsync(
        string jobSegment,
        Guid jobId,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string familyUserId,
        string template);

    /// <summary>
    /// Substitutes job-level tokens only (e.g., !JOBNAME, !USLAXVALIDTHROUGHDATE).
    /// Use this for anonymous/public content like bulletins and menus.
    /// </summary>
    Task<string> SubstituteJobTokensAsync(string jobPath, string template);
}
