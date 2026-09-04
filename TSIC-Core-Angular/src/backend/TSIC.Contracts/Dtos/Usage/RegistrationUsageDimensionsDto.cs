namespace TSIC.Contracts.Dtos.Usage;

/// <summary>
/// The registration facts usage logging needs, resolved for a whole batch in one query.
///
/// Exists so the usage writer never calls a single-row lookup in a loop: a batch of
/// several hundred requests contains far fewer distinct registrations, and one
/// set-based query replaces one round-trip per row.
/// </summary>
public record RegistrationUsageDimensionsDto
{
    public required Guid RegId { get; init; }

    /// <summary>
    /// The job this registration belongs to. NOT NULL on the entity, so every resolved
    /// registration carries one.
    ///
    /// This is the AUTHORITATIVE job for an authenticated request, and it is why the
    /// jobPath lookup is not needed for that traffic: jobPath is a string claim minted
    /// at login, while this is the row's own foreign key. Reading both and hoping they
    /// agree is strictly worse than reading the one that cannot be stale.
    /// </summary>
    public required Guid JobId { get; init; }

    /// <summary>Null when the registration is not rostered to a team.</summary>
    public Guid? AssignedTeamId { get; init; }
}
