using System;
using System.Collections.Generic;

namespace TSIC.Contracts.Dtos;

public sealed record PlayerRegFinancialLineDto(
    Guid RegistrationId,
    string PlayerName,
    string TeamName,
    decimal FeeTotal,
    List<string> DiscountCodes);

public sealed record PlayerRegTsicFinancialDto(
    bool WasImmediateCharge,
    bool WasArb,
    decimal AmountCharged,
    string Currency,
    string? TransactionId,
    string? PaymentMethodMasked,
    DateTime? NextArbBillDate,
    decimal TotalOriginal,
    decimal TotalDiscounts,
    decimal TotalNet,
    List<PlayerRegFinancialLineDto> Lines);

public sealed record PlayerRegPolicyDto(
    Guid RegistrationId,
    string PolicyNumber,
    DateTime IssuedUtc,
    int InsurableAmountCents);

public sealed record PlayerRegInsuranceStatusDto(
    bool Offered,
    bool Selected,
    bool Declined,
    bool PurchaseSucceeded,
    List<PlayerRegPolicyDto> Policies);

public sealed record PlayerRegConfirmationDto(
    PlayerRegTsicFinancialDto Tsic,
    PlayerRegInsuranceStatusDto Insurance,
    string ConfirmationHtml);
