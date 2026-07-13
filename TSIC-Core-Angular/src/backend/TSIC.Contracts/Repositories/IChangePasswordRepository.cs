using TSIC.Contracts.Dtos.ChangePassword;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Data access for the SuperUser Change Password utility.
/// Read <c>docs/Domain/change-password-contract.md</c> before changing anything here — in particular
/// §1 (players never sign in; a merge is not a rename) and §6 (measured non-problems).
/// </summary>
public interface IChangePasswordRepository
{
    // ── Search ──

    Task<List<ChangePasswordSearchResultDto>> SearchPlayerRegistrationsAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default);

    Task<List<ChangePasswordSearchResultDto>> SearchAdultRegistrationsAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default);

    // ── Reset targeting ──

    /// <summary>
    /// Resolve which <c>AspNetUsers</c> row a registration's password reset should land on.
    /// This is THE targeting mechanism: the caller names a target (own login vs family login) and the
    /// server follows the registration's FK. The client never supplies the user id.
    /// Returns null when the registration does not exist or has no account on that side.
    /// </summary>
    Task<ResetTargetDto?> ResolveResetTargetAsync(
        Guid registrationId,
        ResetPasswordTarget target,
        CancellationToken ct = default);

    /// <summary>
    /// Everything the reset dialog must show BEFORE anyone types a password: the account the registration
    /// resolves to, whose it is, and — the line that stops the mistake — what it signs in for.
    /// Returns null when the registration does not exist or has no account on that side.
    /// </summary>
    Task<ResetContextDto?> GetResetContextAsync(
        Guid registrationId,
        ResetPasswordTarget target,
        CancellationToken ct = default);

    // ── Email updates ──

    Task UpdateUserEmailAsync(
        Guid registrationId,
        string? newEmail,
        CancellationToken ct = default);

    Task UpdateFamilyEmailsAsync(
        Guid registrationId,
        string? familyEmail,
        string? momEmail,
        string? dadEmail,
        CancellationToken ct = default);

    // ── Merge ──
    //
    // ONE identity key, defined in TSIC.Domain/Constants/HouseholdIdentity.cs:
    //
    //     email  AND  phone  AND  name       all three, normalized. A placeholder is not an identity.
    //
    // It is a SECURITY control. A merge hands one account's children and history to another, across
    // customers, irreversibly — and the SuperUser pulls the trigger on a list THIS CODE produced, so
    // the candidate list is the security boundary. A miss costs nothing (the parent uses their new
    // account); a false match is a breach. Read that file before widening anything.
    //
    // ONE RETIREMENT PER CALL. Never a list. A parent with four logins is three deliberate merges.

    /// <summary>
    /// Other logins that are the SAME ADULT, IN THE SAME ROLE — their own email + phone + name, and a
    /// registration in this registration's role. A Club Rep never merges with their own Staff login.
    ///
    /// <b>Empty for a player</b>: a player has no login, and a child is collapsed only inside their
    /// household's merge. See <see cref="MergeFamilyRegistrationsAsync"/>.
    /// </summary>
    Task<MergeCandidatesResponse> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Family logins that are the SAME HOUSEHOLD as this registration's — the mother's email, phone and
    /// name all agree. The family account IS her data.
    ///
    /// Not keyed on the child: "owns the same child" says two households OVERLAP (divorced parents
    /// legitimately share one), not that they ARE one.
    ///
    /// More than two candidates is normal — a parent who has forgotten their password twice has three
    /// logins, and they all key to the same mother.
    /// </summary>
    Task<MergeCandidatesResponse> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Retire ONE adult login onto <paramref name="keepUserName"/>. Both names MUST be accounts
    /// <see cref="GetUserMergeCandidatesAsync"/> returned; anything else throws. Only the retiree's
    /// registrations IN THIS REGISTRATION'S ROLE move.
    ///
    /// Returns every registration moved AND the account it moved off — not a count. A count cannot
    /// reverse a merge: afterwards the surviving login owns rows that used to belong to another account
    /// and nothing on the row says which. See <see cref="MergeResultDto"/>.
    /// </summary>
    Task<MergeResultDto> MergeUserRegistrationsAsync(
        Guid registrationId,
        string keepUserName,
        string retireUserName,
        CancellationToken ct = default);

    /// <summary>
    /// Retire ONE family login onto <paramref name="keepUserName"/> — the account the parent asked for.
    /// Both names MUST be accounts <see cref="GetFamilyMergeCandidatesAsync"/> returned; anything else
    /// throws.
    ///
    /// Moves BOTH FKs: <c>Family_UserId</c> for every registration under the retiring login (including
    /// inactive ones — orphaning a parent's dropped registrations on a dead login is how they lose their
    /// history), and <c>UserId</c> for each child that exists unambiguously on both sides, so the parent
    /// does not sign in to find every child listed twice.
    /// </summary>
    Task<MergeResultDto> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string keepUserName,
        string retireUserName,
        CancellationToken ct = default);
}
