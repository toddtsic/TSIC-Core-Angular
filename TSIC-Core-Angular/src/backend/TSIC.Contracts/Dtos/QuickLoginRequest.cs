namespace TSIC.Contracts.Dtos;

/// <summary>
/// Single-call login request that wraps both authentication phases.
/// If RegId is provided, returns an enriched JWT immediately.
/// If RegId is null, returns a minimal JWT + available registrations so the caller can select.
/// </summary>
public record QuickLoginRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    /// <summary>Optional registration ID. If provided, skips the registration selection step.</summary>
    public string? RegId { get; init; }
}

/// <summary>
/// Response for quick-login. Always includes a token.
/// When RegId was provided (or the user has exactly one registration), Registrations is null.
/// When the caller must choose, Registrations is populated and the token is minimal (Phase 1 only).
/// </summary>
public record QuickLoginResponse
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public int? ExpiresIn { get; init; }
    public bool RequiresTosSignature { get; init; }
    /// <summary>Populated only when the caller must choose a registration. Null when login is fully resolved.</summary>
    public List<RegistrationRoleDto>? Registrations { get; init; }
}
