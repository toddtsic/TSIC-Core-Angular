namespace TSIC.Contracts.Dtos.ChangePassword;

// ── Search ──

public record ChangePasswordSearchRequest
{
    public required string RoleId { get; init; }
    public string? CustomerName { get; init; }
    public string? JobName { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? UserName { get; init; }
    public string? FamilyUserName { get; init; }
}

public record ChangePasswordSearchResultDto
{
    public required Guid RegistrationId { get; init; }
    public required string RoleName { get; init; }
    public required string CustomerName { get; init; }
    public required string JobName { get; init; }

    // User account
    public required string UserName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }

    // Family data (Player role only)
    public string? FamilyUserName { get; init; }
    public string? FamilyEmail { get; init; }
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomPhone { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadEmail { get; init; }
    public string? DadPhone { get; init; }
}

// ── Role options ──

public record ChangePasswordRoleOptionDto
{
    public required string RoleId { get; init; }
    public required string RoleName { get; init; }
}

// ── Admin password reset ──

public record AdminResetPasswordRequest
{
    public required string UserName { get; init; }
    public required string NewPassword { get; init; }
}

// ── Email updates ──

public record UpdateUserEmailRequest
{
    public required string Email { get; init; }
}

public record UpdateFamilyEmailsRequest
{
    public string? FamilyEmail { get; init; }
    public string? MomEmail { get; init; }
    public string? DadEmail { get; init; }
}

// ── Merge ──

public record MergeCandidateDto
{
    public required string UserName { get; init; }
    public required string UserId { get; init; }
}

public record MergeUsernameRequest
{
    public required string TargetUserName { get; init; }
}
