namespace TSIC.Contracts.Dtos;

public record ForgotPasswordRequest
{
    /// <summary>
    /// A username or an email address. An email can own several accounts here (a family login plus
    /// the parent's own adult/staff logins), so the lookup returns a LIST and one reset email is
    /// sent per account — legacy AccountController semantics.
    /// </summary>
    public required string UsernameOrEmail { get; init; }
}

public record ResetPasswordRequest
{
    /// <summary>
    /// The reset link is keyed by user id, never email — an email is one-to-many over accounts in
    /// this database, so it cannot identify which account to reset.
    /// </summary>
    public required string UserId { get; init; }
    public required string Token { get; init; }
    public required string NewPassword { get; init; }
}
