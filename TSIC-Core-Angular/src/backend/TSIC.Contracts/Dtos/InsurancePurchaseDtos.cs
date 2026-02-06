using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Request payload for independent RegSaver / VerticalInsure policy purchase.
/// VI payment is entirely handled by VerticalInsure; we only persist resulting policy numbers.
/// JobId and FamilyUserId are derived from JWT claims and NOT accepted as parameters (security principle).
/// </summary>
public record InsurancePurchaseRequestDto
{
    [Required, JsonRequired]
    public required string JobPath { get; init; } = string.Empty;
    [Required, JsonRequired]
    public required List<Guid> RegistrationIds { get; init; } = new();
    [Required, JsonRequired]
    public required List<string> QuoteIds { get; init; } = new();
    /// <summary>
    /// Optional credit card information for direct VerticalInsure batch purchase when a Stripe token is not supplied.
    /// </summary>
    public CreditCardInfo? CreditCard { get; init; }
}

/// <summary>
/// Result of attempting to purchase policies. When Success=false, Error contains user-visible reason.
/// Policies map is only populated on success.
/// </summary>
public record VerticalInsurePurchaseResult
{
    public required bool Success { get; init; }
    public required string? Error { get; init; }
    public required Dictionary<Guid, string> Policies { get; init; } = new();
}

/// <summary>
/// API response DTO for insurance purchase endpoint.
/// </summary>
public record InsurancePurchaseResponseDto
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public required Dictionary<Guid, string> Policies { get; init; } = new();
}
