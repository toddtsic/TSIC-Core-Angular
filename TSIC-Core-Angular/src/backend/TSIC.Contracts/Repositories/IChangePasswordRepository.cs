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

    /// <summary>
    /// Candidate accounts a PLAYER's registrations could be merged onto.
    /// Key: first name + last name + DOB + role. Sound because a player's DOB is never null
    /// (measured 0 of 130,831 — contract §2).
    /// </summary>
    Task<MergeCandidatesResponse> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Candidate FAMILY logins this registration's family could be merged onto.
    ///
    /// Key: <b>the child</b> — another family login that owns a player with the same
    /// (first name, last name, DOB). NOT the parents.
    ///
    /// Legacy keyed this on an exact match of all six parent fields plus postal code, and that finds
    /// only 47% of the real duplicates: households re-register from scratch each season and type mom
    /// and dad into swapped slots, with typos and nicknames ("Su Kang / Jesse Abraham" vs
    /// "Jesse Abraham / Su Kang"). The child is the stable key. Contract §2.
    /// </summary>
    Task<MergeCandidatesResponse> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Re-point <c>Registrations.UserId</c> onto <paramref name="targetUserName"/>.
    /// The target MUST be one of the accounts <see cref="GetUserMergeCandidatesAsync"/> returned;
    /// anything else throws.
    ///
    /// Returns every registration moved AND the account it moved off — not a count. There is no undo
    /// for a merge, and a count cannot be undone: see <see cref="MergeResultDto"/>.
    /// </summary>
    Task<MergeResultDto> MergeUserRegistrationsAsync(
        Guid registrationId,
        string targetUserName,
        CancellationToken ct = default);

    /// <summary>
    /// Re-point <c>Registrations.Family_UserId</c> onto <paramref name="targetFamilyUserName"/>.
    /// The target MUST be one of the accounts <see cref="GetFamilyMergeCandidatesAsync"/> returned;
    /// anything else throws.
    ///
    /// Returns every registration moved AND the account it moved off — see <see cref="MergeResultDto"/>.
    /// </summary>
    Task<MergeResultDto> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string targetFamilyUserName,
        CancellationToken ct = default);
}
