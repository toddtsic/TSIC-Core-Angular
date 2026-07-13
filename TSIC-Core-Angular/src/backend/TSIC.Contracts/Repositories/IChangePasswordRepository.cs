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

    /// <summary>
    /// Other logins that are the SAME ADULT — their own email + phone + name.
    /// <b>Returns empty for a player</b>: a player has no login, and a child is collapsed only inside
    /// their household's merge. See <see cref="MergeFamilyRegistrationsAsync"/>.
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
    /// More than one candidate is normal — a parent who has forgotten their password twice has three
    /// logins, and they all key to the same mother.
    /// </summary>
    Task<MergeCandidatesResponse> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Re-point every registration under <paramref name="sourceUserNames"/> onto
    /// <paramref name="targetUserName"/>. Every name given MUST be one
    /// <see cref="GetUserMergeCandidatesAsync"/> returned; anything else throws.
    ///
    /// Returns every registration moved AND the account it moved off — not a count. A count cannot
    /// reverse a merge: afterwards the target owns rows that used to belong to several accounts and
    /// nothing on the row says which. See <see cref="MergeResultDto"/>.
    /// </summary>
    Task<MergeResultDto> MergeUserRegistrationsAsync(
        Guid registrationId,
        string targetUserName,
        IReadOnlyList<string> sourceUserNames,
        CancellationToken ct = default);

    /// <summary>
    /// Collapse the family logins named in <paramref name="sourceFamilyUserNames"/> onto
    /// <paramref name="targetFamilyUserName"/> — the account the parent asked for. Every name given
    /// MUST be one <see cref="GetFamilyMergeCandidatesAsync"/> returned; anything else throws.
    ///
    /// Moves BOTH FKs: <c>Family_UserId</c> for every registration under a losing login (including
    /// inactive ones — orphaning a parent's dropped registrations on a dead login is how they lose
    /// their history), and <c>UserId</c> for each child that exists unambiguously on both sides, so the
    /// parent does not end up seeing every child twice.
    /// </summary>
    Task<MergeResultDto> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string targetFamilyUserName,
        IReadOnlyList<string> sourceFamilyUserNames,
        CancellationToken ct = default);
}
