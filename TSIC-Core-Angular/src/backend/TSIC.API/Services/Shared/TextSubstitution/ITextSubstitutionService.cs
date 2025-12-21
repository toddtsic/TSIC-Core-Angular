namespace TSIC.API.Services.Shared.TextSubstitution;

public interface ITextSubstitutionService
{
    Task<string> SubstituteAsync(
        string jobSegment,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string familyUserId,
        string template);
}
