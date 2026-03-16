namespace TSIC.Contracts.Dtos.Scheduling;

public record BracketSeedGameDto
{
    public required int Gid { get; init; }
    public required string AgegroupName { get; init; }
    public required int? WhichSide { get; init; }
    public required string T1Type { get; init; }
    public required int T1No { get; init; }
    public required Guid? T1SeedDivId { get; init; }
    public required string? T1SeedDivName { get; init; }
    public required int? T1SeedRank { get; init; }
    public required string T2Type { get; init; }
    public required int T2No { get; init; }
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
}
