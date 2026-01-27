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
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required decimal TotalDiscount { get; init; }
    public required int TotalPlayersProcessed { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required List<PlayerDiscountResult> Results { get; init; }
    // Updated financials for successfully discounted players (key = playerId)
    public Dictionary<string, RegistrationFinancialsDto> UpdatedFinancials { get; set; } = new();
}

public sealed class PlayerDiscountResult
{
    public required string PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required decimal DiscountAmount { get; init; }
}

// Team discount DTOs

public sealed class ApplyTeamDiscountRequestDto
{
    [Required]
    public required string JobPath { get; init; }

    [Required]
    public required string Code { get; init; }

    [Required]
    public required List<Guid> TeamIds { get; init; }
}

public sealed class ApplyTeamDiscountResponseDto
{
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required int TotalTeamsProcessed { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required List<TeamDiscountResult> Results { get; init; }
}

public sealed class TeamDiscountResult
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required int? DiscountCodeId { get; init; }
}
