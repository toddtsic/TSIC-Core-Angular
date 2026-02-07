using System.ComponentModel.DataAnnotations;

namespace TSIC.Contracts.Dtos.DiscountCode;

/// <summary>
/// Request to create a new discount code.
/// </summary>
public record AddDiscountCodeRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public required string CodeName { get; init; }

    [Required]
    public required string DiscountType { get; init; } // "Percentage" or "DollarAmount"

    [Required]
    [Range(0.01, 999999.99)]
    public required decimal Amount { get; init; }

    [Required]
    public required DateTime StartDate { get; init; }

    [Required]
    public required DateTime EndDate { get; init; }
}
