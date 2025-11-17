using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TSIC.API.Dtos;

/// <summary>
/// Request payload for independent RegSaver / VerticalInsure policy purchase.
/// VI payment is entirely handled by VerticalInsure; we only persist resulting policy numbers.
/// </summary>
public class InsurancePurchaseRequestDto
{
    [Required, JsonRequired]
    public Guid JobId { get; set; }
    [Required, JsonRequired]
    public Guid FamilyUserId { get; set; }
    [Required, JsonRequired]
    public List<Guid> RegistrationIds { get; set; } = new();
    [Required, JsonRequired]
    public List<string> QuoteIds { get; set; } = new();
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
