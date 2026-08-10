namespace TSIC.Contracts.Dtos;

/// <summary>
/// Mobile scorer login — credentials plus the event the user picked in the app.
/// Job scoping is fixed at login rather than inferred from whichever registration
/// happened to auto-select, because a Scorer's entire authority is one job.
/// Responds with <see cref="AuthTokenResponse"/>; the token is ALWAYS enriched.
/// There is deliberately no minimal-token fallback — a roleless 200 is exactly what
/// made the previous mobile failure silent (authenticated, then 403 on every score).
/// </summary>
public record ScorerLoginRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }

    /// <summary>The event being scored. Required — never defaulted or inferred.</summary>
    public required Guid JobId { get; init; }
}
