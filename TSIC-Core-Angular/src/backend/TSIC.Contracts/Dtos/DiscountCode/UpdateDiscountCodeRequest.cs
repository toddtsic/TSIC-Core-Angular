using System.ComponentModel.DataAnnotations;

namespace TSIC.Contracts.Dtos.DiscountCode;

/// <summary>
/// Request to update an existing discount code.
/// CodeName and UsageCount cannot be changed (historical integrity).
/// </summary>
public record UpdateDiscountCodeRequest
{
    [Required]
    public required string DiscountType { get; init; } // "Percentage" or "DollarAmount"

    [Required]
    [Range(0.01, 999999.99)]
    public required decimal Amount { get; init; }

    [Required]
    public required DateTime StartDate { get; init; }

    [Required]
    public required DateTime EndDate { get; init; }

    [Required]
    public required bool IsActive { get; init; }
}
