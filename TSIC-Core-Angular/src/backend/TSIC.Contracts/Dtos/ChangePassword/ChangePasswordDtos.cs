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

/// <summary>
/// Which of a registration's two accounts to reset.
///
/// <c>Jobs.Registrations</c> points into <c>AspNetUsers</c> TWICE: <c>UserId</c> (who the
/// registration is about) and <c>Family_UserId</c> (the login that owns it). For a player these are
/// different rows, and only the family one can actually sign in.
/// See <c>docs/Domain/change-password-contract.md</c> §1.
/// </summary>
public enum ResetPasswordTarget
{
    /// <summary>The registrant's own login — <c>Registrations.UserId</c>. Meaningful for adults only;
    /// a player's own credential is vestigial and frequently a raw GUID.</summary>
    User = 0,

    /// <summary>The family login — <c>Registrations.Family_UserId</c>. The only login a parent uses.</summary>
    Family = 1
}

public record AdminResetPasswordRequest
{
    /// <summary>Which account this registration resolves to. The SERVER does the targeting; this
    /// only says which of the registration's two FKs to follow.</summary>
    public required ResetPasswordTarget Target { get; init; }

    public required string NewPassword { get; init; }

    /// <summary>The username the caller believes they are resetting. Checked against the account the
    /// registration actually resolves to; a mismatch is a 400. A guard against a stale UI — NOT the
    /// targeting mechanism.</summary>
    public required string ExpectedUserName { get; init; }
}

/// <summary>The account a reset resolved to, followed from the registration's own FK.</summary>
public record ResetTargetDto
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
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

/// <summary>
/// One login account a merge could re-point registrations onto.
///
/// A merge is NOT a rename. It abandons the source account and moves registrations onto this one,
/// irreversibly. So the admin has to be able to answer "is this really the same person?" — and the
/// username cannot tell them, because half the player usernames in the system are raw GUIDs
/// (<c>76da3519-7842-400e-84ed-4ea6005e974c</c>). Every field below exists to answer that question.
/// </summary>
public record MergeCandidateDto
{
    public required string UserName { get; init; }
    public required string UserId { get; init; }

    /// <summary>Registrations already sitting on this account.</summary>
    public required int RegistrationCount { get; init; }

    /// <summary>This account's own email — F-Email for a family login, R-Email for a person.</summary>
    public string? Email { get; init; }

    /// <summary>The person this account is about. Null for a family login: it has no person of its
    /// own, only the household below.</summary>
    public string? PersonName { get; init; }
    public DateTime? Dob { get; init; }

    /// <summary>The household. For a family candidate, its own parents; for a player candidate, the
    /// family it sits under. Recorded as typed — mom and dad are routinely in swapped slots.</summary>
    public string? MomName { get; init; }
    public string? MomEmail { get; init; }
    public string? DadName { get; init; }
    public string? DadEmail { get; init; }

    /// <summary>The children this family login owns — the evidence that it is the same household.
    /// Empty for a player candidate.</summary>
    public required IReadOnlyList<MergeCandidateChildDto> Children { get; init; }

    /// <summary>Where this account has been used. Identity breadcrumbs; capped.</summary>
    public required IReadOnlyList<string> Jobs { get; init; }
}

/// <summary>A child under a candidate family login.</summary>
public record MergeCandidateChildDto
{
    public required string Name { get; init; }
    public DateTime? Dob { get; init; }

    /// <summary>True when this child also appears under the SOURCE account. This is the match that
    /// made the candidate a candidate — it is the thing the admin is being asked to confirm.</summary>
    public required bool MatchesSource { get; init; }
}

/// <summary>
/// Everything the admin needs before an irreversible merge: who they are merging FROM, what they can
/// merge INTO, and — the part legacy never showed — how much the merge will actually move.
/// </summary>
public record MergeCandidatesResponse
{
    /// <summary>The account being merged away.</summary>
    public required MergeCandidateDto Source { get; init; }

    /// <summary>Accounts it could be merged into.</summary>
    public required IReadOnlyList<MergeCandidateDto> Candidates { get; init; }

    /// <summary>
    /// Registrations the merge will re-point — across EVERY account matching the identity key, not
    /// just the source's.
    ///
    /// The wide net is deliberate and correct: a child accumulates one account per season, so this
    /// consolidates all of them in a single action instead of forcing the admin to repeat it six
    /// times. It is surfaced here because a blast radius that large must be SEEN, not implied.
    /// </summary>
    public required int RegistrationsAffected { get; init; }

    /// <summary>How many distinct accounts those registrations sit on today (including the source).</summary>
    public required int AccountsAffected { get; init; }
}

public record MergeUsernameRequest
{
    public required string TargetUserName { get; init; }
}

/// <summary>
/// One registration a merge re-pointed, and the account it was re-pointed AWAY from.
///
/// <c>PreviousUserId</c> is per-registration and not redundant: the person merge SWEEPS every account
/// matching the identity key, so a single merge can pull registrations off half a dozen different
/// seasonal accounts at once. There is no one "old owner" to record.
/// </summary>
public record MergedRegistrationDto
{
    public required Guid RegistrationId { get; init; }

    /// <summary>The <c>UserId</c> (person merge) or <c>Family_UserId</c> (family merge) this
    /// registration carried before the merge. Restoring this value is the undo.</summary>
    public required string PreviousUserId { get; init; }
}

/// <summary>
/// What a merge actually did — the audit payload.
///
/// The count is NOT enough to reverse a merge, which is the whole reason this type exists. The target
/// account normally already owns registrations of its own, so afterwards "12 rows moved onto B" leaves
/// you unable to say WHICH of B's rows used to be someone else's. <see cref="Moved"/> is the reversal
/// key, and it is logged in full.
///
/// Not returned to the browser — the endpoint answers with a message. This exists so the audit line
/// has something true to say.
/// </summary>
public record MergeResultDto
{
    public required string TargetUserId { get; init; }
    public required string TargetUserName { get; init; }
    public required IReadOnlyList<MergedRegistrationDto> Moved { get; init; }
}
