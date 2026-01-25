using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TSIC.Contracts.Dtos;

public sealed class ApplyDiscountItemDto
{
    [Required]
    public string PlayerId { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }
}

public sealed class ApplyDiscountRequestDto
{
    [Required]
    public string JobPath { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public List<ApplyDiscountItemDto> Items { get; set; } = new();
}

public sealed class ApplyDiscountResponseDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public decimal TotalDiscount { get; set; }
    public Dictionary<string, decimal> PerPlayer { get; set; } = new();
    // Optional: updated financials per playerId (key = playerId/UserId)
    public Dictionary<string, RegistrationFinancialsDto> UpdatedFinancials { get; set; } = new();
}
