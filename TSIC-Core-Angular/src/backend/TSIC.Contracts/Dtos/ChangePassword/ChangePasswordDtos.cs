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

    /// <summary>
    /// How many OTHER logins key to this row's account. <b>Zero means no merge is possible</b>, and the
    /// UI must not offer one — an admin should never open a merge dialog to be told there is nothing in
    /// it.
    ///
    /// Computed against the WHOLE database, not the result set: the search is an anchor, not a census,
    /// and the duplicate login is routinely one the search never returned (that is the entire point of
    /// the identity key). Same rule the merge itself uses, so the number on the button is the number in
    /// the dialog — placeholders excluded, and an account that owns no registrations is not a candidate.
    /// </summary>
    public required int MergeCandidateCount { get; init; }
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

/// <summary>
/// Everything the reset dialog must show BEFORE anyone types a password.
///
/// The row the admin clicked is a REGISTRATION — for a player, that is the child. The account being
/// reset is a different row entirely: a player has no usable login, so the credential that matters is
/// the FAMILY's. Legacy shipped a <c>NewUserPassword</c> field on player rows that reset a password
/// nobody can sign in with.
///
/// So the dialog says, in this order: the row you clicked → the account you are changing → what that
/// account signs in for. The last one is what stops the mistake.
/// </summary>
public record ResetContextDto
{
    public required ResetPasswordTarget Target { get; init; }

    /// <summary>The login being changed.</summary>
    public required string UserName { get; init; }
    public string? Email { get; init; }

    /// <summary>Whose login it is — the MOTHER for a family login, the person themselves for an adult.</summary>
    public string? OwnerName { get; init; }
    public string? OwnerPhone { get; init; }

    /// <summary>True when this is a family login, i.e. the row the admin clicked was a player.</summary>
    public required bool IsFamilyLogin { get; init; }

    /// <summary>
    /// What signing in with this account actually reaches. For a family login: the children it can
    /// select. For an adult: the roles and events they hold — which is what says WHICH John Smith.
    /// </summary>
    public required IReadOnlyList<AccountReachDto> SignsInFor { get; init; }
}

/// <summary>One line of "what this login reaches".</summary>
public record AccountReachDto
{
    /// <summary>A child's name (family login), or a role name (adult).</summary>
    public required string Label { get; init; }

    /// <summary>The child's date of birth. Null for an adult line.</summary>
    public DateTime? Dob { get; init; }

    /// <summary><c>Steps Lacrosse — Fall League 2025</c>. Null for a family login's child line.</summary>
    public string? Where { get; init; }
}

// ── Contact updates ──
//
// PATCH semantics throughout, established by 5a121a2c: an OMITTED field (null) means "leave it alone";
// an EMPTY field ("") means "clear it". Do not collapse the two — collapsing them is what made a stale
// address unremovable, and it is also what would silently clear somebody's `not@given.com` opt-out.

/// <summary>An adult IS their own account, so their email and phone are their own AspNetUsers row.</summary>
public record UpdateUserContactRequest
{
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
}

/// <summary>
/// A player's contacts belong to the HOUSEHOLD, not the child — so Ann edits the <c>Families</c> row,
/// and only that: mother and father, email and phone.
///
/// There is deliberately no "family login email" field. The family login IS the mother (contract §1),
/// so its <c>AspNetUsers</c> row is not an independent thing to type into — it is brought to PARITY with
/// her by the server. Letting an admin edit it separately is how the two drift apart, and when they
/// drift the mother's password reset goes to whichever address the login happens to hold.
/// </summary>
public record UpdateFamilyContactsRequest
{
    public string? MomEmail { get; init; }
    public string? MomCellphone { get; init; }
    public string? DadEmail { get; init; }
    public string? DadCellphone { get; init; }
}

// ── Merge ──

/// <summary>
/// One login that keys to the identity — a candidate for either side of the merge.
///
/// A merge is NOT a rename. It moves one login's registrations onto another and leaves the first owning
/// nothing, irreversibly. So the admin has to answer "is this really the same person?" — and the
/// username cannot tell them, because half the player usernames in the system are raw GUIDs
/// (<c>76da3519-7842-400e-84ed-4ea6005e974c</c>).
///
/// Two dropdowns of GUIDs is not enough, and that is what every field below is for. The merge moves
/// REGISTRATIONS, so the admin gets to look at the registrations before they move.
/// </summary>
public record MergeCandidateDto
{
    public required string UserName { get; init; }
    public required string UserId { get; init; }

    // ── The identity block. THIS IS THE KEY, SHOWN. ──
    // The admin compares it across the two panels: if these do not look like the same person, they
    // stop. Recorded as typed — mom and dad are routinely in swapped slots.

    /// <summary>The mother. A family account IS her data. Null for an adult.</summary>
    public string? MomName { get; init; }
    public string? MomEmail { get; init; }
    public string? MomPhone { get; init; }
    public string? DadName { get; init; }
    public string? DadEmail { get; init; }

    /// <summary>The adult themselves — they sign in as themselves. Null for a family login.</summary>
    public string? PersonName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }

    /// <summary>The children this family login owns. Empty for an adult.</summary>
    public required IReadOnlyList<MergeCandidateChildDto> Children { get; init; }

    /// <summary>
    /// EVERY registration on this account — not a count.
    ///
    /// If this account is retired, all of these move. The admin is about to relocate a family's entire
    /// history, so they see it first. Legacy told them a number, or nothing.
    /// </summary>
    public required IReadOnlyList<MergeCandidateRegistrationDto> Registrations { get; init; }
}

/// <summary>A child under a candidate family login. Whether it MATCHES the other panel is derived in
/// the browser — it depends on which pair the admin has selected, which the server does not know.</summary>
public record MergeCandidateChildDto
{
    public required string UserId { get; init; }
    public required string Name { get; init; }
    public DateTime? Dob { get; init; }
}

/// <summary>One registration a merge would move.</summary>
public record MergeCandidateRegistrationDto
{
    public required Guid RegistrationId { get; init; }
    public required string CustomerName { get; init; }
    public required string JobName { get; init; }
    public required string RoleName { get; init; }

    /// <summary>Who it is about — the child, under a family login.</summary>
    public string? PersonName { get; init; }
}

/// <summary>The identity every candidate keys to. Shown as the dialog's header.</summary>
public record MergeIdentityDto
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Phone { get; init; }
}

/// <summary>
/// The logins that key to one identity — the input to the merge dialog.
///
/// There is no "source" and no "target" here. The admin picks BOTH from this one list: the parent named
/// the survivor on the phone, and the admin retires one other login at a time.
/// </summary>
public record MergeCandidatesResponse
{
    /// <summary>
    /// What they all key to. <b>Null means the account has no identity</b> — a blank, malformed or
    /// placeholder contact block — in which case <see cref="Accounts"/> is empty and no merge is
    /// possible. That is the correct, safe outcome.
    /// </summary>
    public MergeIdentityDto? Identity { get; init; }

    /// <summary>
    /// Every login keying to that identity, INCLUDING the one the search landed on. Two or more means
    /// a merge is possible; fewer means there is nothing to merge.
    ///
    /// More than two is normal: a parent who has forgotten their password twice has three logins, and
    /// the worst real household on file has eleven.
    /// </summary>
    public required IReadOnlyList<MergeCandidateDto> Accounts { get; init; }

    /// <summary>Adult merges only — the role the candidates are constrained to. Adults merge WITHIN a
    /// role: a Club Rep never merges with their own Staff account.</summary>
    public string? RoleName { get; init; }
}

/// <summary>
/// One merge: keep this login, retire that one.
///
/// <b>Exactly one retirement per act, never a list.</b> A parent with four logins is three deliberate
/// merges — three confirmations, three audit lines, three independently reversible operations. A
/// multi-select would put a dozen irreversible cross-tenant writes behind one button.
///
/// Both names are re-validated server-side against the candidate set the identity key produces, so the
/// browser cannot introduce an account the key never approved.
/// </summary>
public record MergeUsernameRequest
{
    /// <summary>The survivor — the login the parent asked us to keep. Everything lands here.</summary>
    public required string KeepUserName { get; init; }

    /// <summary>The ONE login being retired. Its registrations move; afterwards it owns nothing.</summary>
    public required string RetireUserName { get; init; }
}

/// <summary>
/// One registration a merge re-pointed, and the account it was re-pointed AWAY from.
/// Restoring <c>PreviousUserId</c> is the undo.
/// </summary>
public record MergedRegistrationDto
{
    public required Guid RegistrationId { get; init; }
    public required string PreviousUserId { get; init; }
}

/// <summary>
/// What a merge actually did — the audit payload.
///
/// The count is NOT enough to reverse a merge, which is the whole reason this type exists. The surviving
/// account normally already owns registrations of its own, so afterwards "9 rows moved onto B" leaves
/// you unable to say WHICH of B's rows used to be someone else's. <see cref="Moved"/> is the reversal
/// key, and it is logged in full.
///
/// Not returned to the browser — the endpoint answers with a message. This exists so the audit line
/// has something true to say.
/// </summary>
public record MergeResultDto
{
    public required string KeepUserId { get; init; }
    public required string KeepUserName { get; init; }
    public required string RetireUserName { get; init; }
    public required IReadOnlyList<MergedRegistrationDto> Moved { get; init; }

    /// <summary>Children collapsed onto the surviving account's record, so the parent does not end up
    /// seeing every child twice.</summary>
    public required int ChildrenCollapsed { get; init; }
}
