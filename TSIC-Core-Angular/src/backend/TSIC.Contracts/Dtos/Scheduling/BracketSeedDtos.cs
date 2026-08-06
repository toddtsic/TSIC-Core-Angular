namespace TSIC.Contracts.Dtos.Scheduling;

/// <summary>
/// The bracket-seeds board: the games plus whether this job reseeds across agegroups
/// (Jobs.bReseedTournament). In reseed mode the client offers job-wide pools + a pool-sized
/// rank list; otherwise the same-agegroup picker with a static rank list.
/// </summary>
public record BracketSeedBoardDto
{
    public required bool IsReseed { get; init; }
    public required List<BracketSeedGameDto> Games { get; init; }
}

public record BracketSeedGameDto
{
    public required int Gid { get; init; }
    public required string AgegroupName { get; init; }
    /// <summary>Owning division — the scope seedability is derived within. Null on unplaced games.</summary>
    public required Guid? DivId { get; init; }
    public required string T1Type { get; init; }
    public required int T1No { get; init; }

    /// <summary>
    /// True when this slot is an entry point from pool play — a director must seed it.
    /// False when the slot is fed by an earlier bracket game (a parent-type game in the
    /// same division carries this slot number), so seeding it would be meaningless.
    /// Derived from bracket structure by the service; the repository always emits false.
    /// </summary>
    public required bool T1Seedable { get; init; }
    public required Guid? T1SeedDivId { get; init; }
    public required string? T1SeedDivName { get; init; }
    public required int? T1SeedRank { get; init; }
    public required string T2Type { get; init; }
    public required int T2No { get; init; }
    public required bool T2Seedable { get; init; }
    public required Guid? T2SeedDivId { get; init; }
    public required string? T2SeedDivName { get; init; }
    public required int? T2SeedRank { get; init; }
}

public record UpdateBracketSeedRequest
{
    public required int Gid { get; init; }
    public Guid? T1SeedDivId { get; init; }
    public int? T1SeedRank { get; init; }
    public Guid? T2SeedDivId { get; init; }
    public int? T2SeedRank { get; init; }
}

public record BracketSeedDivisionOptionDto
{
    public required Guid DivId { get; init; }
    public required string DivName { get; init; }

    /// <summary>
    /// Owning agegroup name — populated only in reseed mode, where the pool list spans
    /// agegroups and the label reads "{AgegroupName}: {DivName}". Null for the same-agegroup picker.
    /// </summary>
    public string? AgegroupName { get; init; }
}

