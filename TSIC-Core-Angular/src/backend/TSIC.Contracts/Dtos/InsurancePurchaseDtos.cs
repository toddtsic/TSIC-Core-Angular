using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// Request payload for independent RegSaver / VerticalInsure policy purchase.
/// VI payment is entirely handled by VerticalInsure; we only persist resulting policy numbers.
/// JobId and FamilyUserId are derived from JWT claims and NOT accepted as parameters (security principle).
/// </summary>
public class InsurancePurchaseRequestDto
{
    [Required, JsonRequired]
    public string JobPath { get; set; } = string.Empty;
    [Required, JsonRequired]
    public List<Guid> RegistrationIds { get; set; } = new();
    [Required, JsonRequired]
    public List<string> QuoteIds { get; set; } = new();
    /// <summary>
    /// Optional credit card information for direct VerticalInsure batch purchase when a Stripe token is not supplied.
    /// </summary>
    public CreditCardInfo? CreditCard { get; set; }
}

/// <summary>
/// Result of attempting to purchase policies. When Success=false, Error contains user-visible reason.
/// Policies map is only populated on success.
/// </summary>
public class VerticalInsurePurchaseResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<Guid, string> Policies { get; set; } = new();
}

/// <summary>
/// API response DTO for insurance purchase endpoint.
/// </summary>
public class InsurancePurchaseResponseDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Dictionary<Guid, string> Policies { get; set; } = new();
}
