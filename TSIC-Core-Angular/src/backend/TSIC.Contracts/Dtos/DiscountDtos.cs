using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace TSIC.Contracts.Dtos;

public sealed record ApplyDiscountItemDto
{
    [Required]
    public required string PlayerId { get; init; } = string.Empty;

    [Range(0, double.MaxValue)]
    public required decimal Amount { get; init; }
}

public sealed record ApplyDiscountRequestDto
{
    [Required]
    public required string JobPath { get; init; } = string.Empty;

    [Required]
    public required string Code { get; init; } = string.Empty;

    [Required]
    public required List<ApplyDiscountItemDto> Items { get; init; } = new();
}

public sealed record ApplyDiscountResponseDto
{
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required decimal TotalDiscount { get; init; }
    public required int TotalPlayersProcessed { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required List<PlayerDiscountResult> Results { get; init; }
    // Updated financials for successfully discounted players (key = playerId)
    public required Dictionary<string, RegistrationFinancialsDto> UpdatedFinancials { get; init; } = new();
}

public sealed record PlayerDiscountResult
{
    public required string PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required decimal DiscountAmount { get; init; }
}

// Team discount DTOs

public sealed record ApplyTeamDiscountRequestDto
{
    [Required]
    public required string JobPath { get; init; }

    [Required]
    public required string Code { get; init; }

    [Required]
    public required List<Guid> TeamIds { get; init; }
}

public sealed record ApplyTeamDiscountResponseDto
{
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required int TotalTeamsProcessed { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required List<TeamDiscountResult> Results { get; init; }
}

public sealed record TeamDiscountResult
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required bool Success { get; init; }
    public required string? Message { get; init; }
    public required int? DiscountCodeId { get; init; }
}
