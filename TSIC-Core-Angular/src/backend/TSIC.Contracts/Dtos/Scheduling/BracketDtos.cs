using System;

namespace TSIC.Contracts.Dtos.Scheduling;

/// <summary>
/// A bracket game as placed on the schedule grid (i.e. not a round-robin "T"
/// game). Its stable identity within a division's bracket is
/// <see cref="MinLabel"/> — the min of its two slot numbers — which is the
/// same carry-forward label the engine advances winners on.
/// </summary>
public record PlacedBracketGame
{
    public required int Gid { get; init; }

    /// <summary>Ladder round: Z,Y,X,Q,S,F (or 'B' bronze).</summary>
    public required string RoundType { get; init; }

    public required int Slot1No { get; init; }

    public required int Slot2No { get; init; }

    /// <summary>Match key against a template game's computed min-label.</summary>
    public int MinLabel => Math.Min(Slot1No, Slot2No);
}

/// <summary>A division that has bracket games but no materialized wiring yet.</summary>
public record BracketBackfillTarget
{
    public required Guid AgegroupId { get; init; }
    public required Guid DivId { get; init; }
}

/// <summary>
/// A single seeded slot awaiting a team, and the (division, rank) that fills it.
/// Read straight from the director's seed intent in Leagues.BracketSeeds — one
/// per side of a game that carries a (SeedDivId, SeedRank). Consumed by seed
/// resolution to place the standings-ranked team onto the schedule row.
///
/// Deliberately independent of bracket topology: a slot is seeded because the
/// director gave it a rank, not because it sits in a template. That is what lets a
/// consolation game — rank-filled, never advanced, in no bracket — resolve by the
/// very same path as a ladder leaf.
/// </summary>
public record SeedSlotToResolve
{
    public required int Gid { get; init; }

    /// <summary>1 = T1 side, 2 = T2 side of the target game.</summary>
    public required byte TargetSlot { get; init; }

    /// <summary>The pool the seed is drawn from (may differ from the game's own division — cross-pool).</summary>
    public required Guid SeedDivId { get; init; }

    /// <summary>1-based standings rank within <see cref="SeedDivId"/>.</summary>
    public required int SeedRank { get; init; }
}

/// <summary>
/// Source team identity stamped onto a flight's internal placeholder team during
/// cross-agegroup reseeding: the raw <see cref="TeamName"/> and the
/// <see cref="ClubrepRegistrationid"/> (which drives club/college resolution).
/// </summary>
public record TeamSeedIdentity
{
    public required Guid TeamId { get; init; }
    public required string? TeamName { get; init; }
    public required Guid? ClubrepRegistrationid { get; init; }
}

/// <summary>Target division for a dev-only bracket exercise action.</summary>
public record BracketDevActionRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid DivId { get; init; }
}

/// <summary>Target agegroup for a dev-only agegroup-scope revert.</summary>
public record AgegroupScopeRequest
{
    public required Guid AgegroupId { get; init; }
}

/// <summary>Result of a dev-only bracket exercise action.</summary>
public record BracketDevActionResult
{
    public required int GamesAffected { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// A selectable championship bracket strategy (brackets.Strategies) — drives the
/// format picker in Manage Pairings. Only <see cref="IsActive"/> strategies are offered.
/// </summary>
public record BracketStrategyDto
{
    /// <summary>Strategy code, e.g. "SE" (single elimination). Sent back on generate.</summary>
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
}

/// <summary>
/// A materialized bracket instance with its display names + template facts, for QA.
/// </summary>
public record BracketInstanceInfo
{
    public required int BracketInstanceId { get; init; }
    public required Guid JobId { get; init; }
    public required Guid AgegroupId { get; init; }
    public required Guid DivId { get; init; }
    public required string AgegroupName { get; init; }
    public required string DivName { get; init; }
    public required int TemplateId { get; init; }
    public required int BracketSize { get; init; }
    public required string StrategyCode { get; init; }
}

/// <summary>Outcome of a bracket-metadata recompute for one division. Only the
/// advancement feed graph is materialized here — seed intent is not projected;
/// it is read live from Leagues.BracketSeeds by seed resolution.</summary>
public record BracketGenerationResult
{
    public required int BracketInstanceId { get; init; }
    public required int BracketSize { get; init; }
    public required int GamesPlaced { get; init; }
    public required int FeedsWritten { get; init; }
}
