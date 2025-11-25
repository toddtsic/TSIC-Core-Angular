namespace TSIC.API.Services;

public interface ITextSubstitutionService
{
    Task<string> SubstituteAsync(
        string jobSegment,
        Guid paymentMethodCreditCardId,
        Guid? registrationId,
        string familyUserId,
        string template);
}
