namespace TSIC.Contracts.Constants;

/// <summary>
/// Payment method codes for Jobs.PaymentMethodsAllowedCode.
/// Simple enum (NOT a bitmask).
/// </summary>
public static class PaymentMethodConstants
{
    public const int CreditCardOnly = 1;
    public const int CreditCardOrCheck = 2;
    public const int CheckOnly = 3;
}
