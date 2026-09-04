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

    /// <summary>Null when the registration is not rostered to a team.</summary>
    public Guid? AssignedTeamId { get; init; }
}
