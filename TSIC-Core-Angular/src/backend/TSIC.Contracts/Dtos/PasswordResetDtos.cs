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

public record ForgotPasswordResponse
{
    public required string Message { get; init; }

    /// <summary>
    /// Populated ONLY when ASPNETCORE_ENVIRONMENT=Development (local vscode). Off-production the
    /// reset email is suppressed by the sandbox gate, so the flow is untestable without the link —
    /// this hands it to the tester in the UI instead. Never populated on Staging: dev.* is
    /// client-facing, and returning a working reset link to an anonymous caller there would be an
    /// account-takeover hole. Empty in every non-Development environment.
    /// (Non-nullable list: a nullable List&lt;T&gt;? here generates an untyped any[] on the
    /// frontend — same reason AdminClubRenameResponse.PerJob is shaped this way.)
    /// </summary>
    public IReadOnlyList<DevResetLink> DevResetLinks { get; init; } = [];
}

public record DevResetLink
{
    public required string UserName { get; init; }
    public required string ResetUrl { get; init; }
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
