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
/// A single leaf bracket slot awaiting a team, and the (division, rank) that
/// fills it. Produced by joining SeedAssignments to its BracketInstance; consumed
/// by seed resolution to place the standings-ranked team onto the schedule row.
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

/// <summary>Target division for a dev-only bracket exercise action.</summary>
public record BracketDevActionRequest
{
    public required Guid AgegroupId { get; init; }
    public required Guid DivId { get; init; }
}

/// <summary>Result of a dev-only bracket exercise action.</summary>
public record BracketDevActionResult
{
    public required int GamesAffected { get; init; }
    public required string Message { get; init; }
}

/// <summary>Outcome of a bracket-metadata recompute for one division.</summary>
public record BracketGenerationResult
{
    public required int BracketInstanceId { get; init; }
    public required int BracketSize { get; init; }
    public required int GamesPlaced { get; init; }
    public required int FeedsWritten { get; init; }
    public required int SeedsWritten { get; init; }
}
