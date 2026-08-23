namespace TSIC.Contracts.Dtos.Stp;

/// <summary>
/// One club rep on a Stay-to-Play event, with the team counts a housing vendor
/// sizes room blocks from. Ports the legacy STPClubReps grid (Controllers/STP/Admin).
///
/// Deliberately NOT carried over from legacy's club-rep contact export: the rep's
/// home street address, city and state. A Stay-to-Play vendor needs to REACH the
/// rep (email, cell) and know where the club travels from (zip) — not where they live.
/// </summary>
public record StpClubRepDto
{
    public required Guid RegistrationId { get; init; }
    public required string ClubName { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required string Cellphone { get; init; }
    public required string ZipCode { get; init; }

    /// <summary>Teams that are active and in a real playing agegroup — the room-block number.</summary>
    public required int ActiveTeamCount { get; init; }

    /// <summary>Teams parked in a "WAITLIST - {agegroup}" mirror bucket.</summary>
    public required int WaitlistedTeamCount { get; init; }

    /// <summary>Teams in the Dropped Teams graveyard.</summary>
    public required int DroppedTeamCount { get; init; }

    public required DateTime RegisteredOn { get; init; }
    public required string JobName { get; init; }
}
