using System.ComponentModel.DataAnnotations;

namespace TSIC.Contracts.Dtos.DiscountCode;

/// <summary>
/// Request to batch activate/deactivate discount codes.
/// </summary>
public record BatchUpdateStatusRequest
{
    [Required]
    [MinLength(1)]
    public required List<int> CodeIds { get; init; }

    [Required]
    public required bool IsActive { get; init; }
}
