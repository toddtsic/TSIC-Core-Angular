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

/// <summary>Outcome of a bracket-metadata recompute for one division.</summary>
public record BracketGenerationResult
{
    public required int BracketInstanceId { get; init; }
    public required int BracketSize { get; init; }
    public required int GamesPlaced { get; init; }
    public required int FeedsWritten { get; init; }
    public required int SeedsWritten { get; init; }
}
