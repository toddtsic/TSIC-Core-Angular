namespace TSIC.Contracts.Dtos;

/// <summary>
/// Whether a candidate username is free to register. Consumed by the registration
/// wizards to warn a user BEFORE they fill out the whole form. Advisory only — the
/// authoritative uniqueness gate stays Identity's <c>UserManager.CreateAsync</c> at
/// account creation, which this pre-check can lose a race to under concurrency.
/// </summary>
public record UsernameAvailabilityResponse
{
    public required bool Available { get; init; }
}
