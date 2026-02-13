namespace TSIC.Contracts.Dtos.Scheduling;

// ── Response DTOs ──

/// <summary>
/// A single pairing record (round-robin or bracket) for display in the pairing grid.
/// </summary>
public record PairingDto
{
    public required int Ai { get; init; }
    public required int GameNumber { get; init; }
    public required int Rnd { get; init; }
    public required int T1 { get; init; }
    public required int T2 { get; init; }
    public required string T1Type { get; init; }
    public required string T2Type { get; init; }
    public int? T1GnoRef { get; init; }
    public int? T2GnoRef { get; init; }
    public string? T1CalcType { get; init; }
    public string? T2CalcType { get; init; }
    public string? T1Annotation { get; init; }
    public string? T2Annotation { get; init; }
    /// <summary>false if this pairing has already been scheduled as a game.</summary>
    public required bool BAvailable { get; init; }
}

/// <summary>
/// Summary of a division for the agegroup→division navigator.
/// </summary>
public record DivisionSummaryDto
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required int TeamCount { get; init; }
}

/// <summary>
/// An agegroup with its child divisions, for the navigator tree.
/// </summary>
public record AgegroupWithDivisionsDto
{
    public required Guid AgegroupId { get; init; }
    public required string AgegroupName { get; init; }
    public required byte SortAge { get; init; }
    public string? Color { get; init; }
    public required List<DivisionSummaryDto> Divisions { get; init; }
}

/// <summary>
/// All pairings for a selected division.
/// </summary>
public record DivisionPairingsResponse
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }
    public required int TeamCount { get; init; }
    public required List<PairingDto> Pairings { get; init; }
}

/// <summary>
/// N×N matrix showing how many times each pair of teams plays each other.
/// </summary>
public record WhoPlaysWhoResponse
{
    public required int TeamCount { get; init; }
    /// <summary>matrix[i][j] = number of games between team (i+1) and team (j+1).</summary>
    public required int[][] Matrix { get; init; }
}

// ── Request DTOs ──

/// <summary>
/// Add a round-robin block of pairings from Masterpairingtable templates.
/// </summary>
public record AddPairingBlockRequest
{
    /// <summary>Number of rounds to generate (1–14).</summary>
    public required int NoRounds { get; init; }
    /// <summary>Team count for this division.</summary>
    public required int TeamCount { get; init; }
}

/// <summary>
/// Add single-elimination bracket pairings, cascading from startKey through Finals.
/// </summary>
public record AddSingleEliminationRequest
{
    /// <summary>Starting bracket key: Z, Y, X, Q, S, or F.</summary>
    public required string StartKey { get; init; }
    /// <summary>Team count for this division.</summary>
    public required int TeamCount { get; init; }
}

/// <summary>
/// Add one blank pairing row for manual entry.
/// </summary>
public record AddSinglePairingRequest
{
    public required int TeamCount { get; init; }
}

/// <summary>
/// Inline edit of an existing pairing row.
/// </summary>
public record EditPairingRequest
{
    public required int Ai { get; init; }
    public int? GameNumber { get; init; }
    public int? Rnd { get; init; }
    public int? T1 { get; init; }
    public int? T2 { get; init; }
    public string? T1Type { get; init; }
    public string? T2Type { get; init; }
    public int? T1GnoRef { get; init; }
    public int? T2GnoRef { get; init; }
    public string? T1CalcType { get; init; }
    public string? T2CalcType { get; init; }
    public string? T1Annotation { get; init; }
    public string? T2Annotation { get; init; }
}

/// <summary>
/// Remove ALL pairings for a given team count within the current league-season.
/// </summary>
public record RemoveAllPairingsRequest
{
    public required int TeamCount { get; init; }
}

// ── Division Teams DTOs (shown alongside pairings) ──

/// <summary>
/// A team in a division with its rank and club name, for the Division Teams table.
/// </summary>
public record DivisionTeamDto
{
    public required Guid TeamId { get; init; }
    public required int DivRank { get; init; }
    public string? ClubName { get; init; }
    public string? TeamName { get; init; }
}

/// <summary>
/// Edit a team's rank and/or name within a division.
/// Changing rank performs an atomic swap with the team currently at that rank.
/// </summary>
public record EditDivisionTeamRequest
{
    public required Guid TeamId { get; init; }
    public required int DivRank { get; init; }
    public string? TeamName { get; init; }
}
