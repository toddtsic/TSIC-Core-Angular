using System.ComponentModel.DataAnnotations;

namespace TSIC.Contracts.Dtos.DiscountCode;

/// <summary>
/// Request to generate multiple discount codes with a pattern.
/// </summary>
public record BulkAddDiscountCodeRequest
{
    [StringLength(20)]
    public string Prefix { get; init; } = string.Empty;

    [StringLength(20)]
    public string Suffix { get; init; } = string.Empty;

    [Required]
    [Range(1, 9999)]
    public required int StartNumber { get; init; }

    [Required]
    [Range(1, 500)]
    public required int Count { get; init; }

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
