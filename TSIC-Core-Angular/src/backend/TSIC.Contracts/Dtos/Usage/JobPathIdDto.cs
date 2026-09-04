namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// One jobPath and the JobId it resolves to, for the batched lookup the usage writer
/// uses on ANONYMOUS traffic.
///
/// The path is carried back deliberately: the caller asked about a set of paths and
/// has to match each answer to the rows that supplied it. Returning ids alone would
/// force matching by position, which is only correct if the database returns rows in
/// the order they were asked for -- it makes no such promise, and a query that skips
/// unresolvable paths returns fewer rows than it was given anyway.
/// </summary>
public record JobPathIdDto
{
    public required string JobPath { get; init; }

    public required Guid JobId { get; init; }
}
