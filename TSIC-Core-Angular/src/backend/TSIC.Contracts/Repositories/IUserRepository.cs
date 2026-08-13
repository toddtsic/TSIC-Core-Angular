using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing AspNetUsers entity data access.
/// Encapsulates all EF Core queries related to user accounts.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Get an AspNetUser by user ID
    /// </summary>
    Task<AspNetUsers?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Terms of Service status for a user by username
    /// </summary>
    /// <returns>True if TOS signature is required (not signed, null, or expired > 1 year)</returns>
    Task<bool> RequiresTosSignatureAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update Terms of Service acceptance for a user by username
    /// </summary>
    Task UpdateTosAcceptanceAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update Terms of Service acceptance for a user by user ID
    /// </summary>
    Task UpdateTosAcceptanceByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get users by IDs for family queries.
    /// Returns list of users with basic info (FirstName, LastName, Email, Birthdate).
    /// </summary>
    Task<List<UserBasicInfo>> GetUsersByIdsAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user names by user IDs for display purposes.
    /// </summary>
    Task<Dictionary<string, UserNameInfo>> GetUserNameMapAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user contact information for form prefill (payment forms, etc.)
    /// </summary>
    Task<UserContactInfo?> GetUserContactInfoAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get users with profile data for family flows (names, contact info, DOB, gender)
    /// </summary>
    Task<List<AspNetUsers>> GetUsersForFamilyAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search admin-candidate accounts by username, first name, or last name (case-insensitive contains).
    /// Two qualifying shapes (both endpoints are SuperUserOnly — the ruling of 2026-08-13
    /// replaced the customer-scoped AM-004 funnel with a platform-wide one):
    /// (a) lane-pure admin — every LIVE registration (bActive, job unexpired for the role type:
    /// admin roles ride Jobs.ExpiryAdmin, everything else Jobs.ExpiryUsers) lies within
    /// <paramref name="laneRoleIds"/>; or (b) pending coach — the account holds EXACTLY ONE
    /// active Unassigned Adult registration, on any job of any customer, with no expiry gate
    /// (the add path deletes that row, so a closed source event is irrelevant). Other roles on
    /// the account do not disqualify shape (b).
    /// Family credential holders (Registrations.FamilyUserId) are excluded globally — a shared
    /// household login must never hold an admin role.
    /// Lane-pure accounts holding a BLOCKING registration on <paramref name="jobId"/> (active or
    /// not — mirrors the add-side duplicate guard) are excluded: blocking = the
    /// <paramref name="requestedRoleId"/> itself, or any role outside the lane. Within the
    /// Director/SuperDirector lane the OTHER role does not block — dual-hat accounts hold both
    /// roles on one job as two registrations. Pending coaches are excluded only when they already
    /// hold the requested role on this job.
    /// </summary>
    Task<List<UserSearchResult>> SearchAdminCandidatesAsync(
        string query,
        Guid jobId,
        string requestedRoleId,
        IReadOnlyCollection<string> laneRoleIds,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explains why <see cref="SearchAdminCandidatesAsync"/> came back empty, so the UI can show
    /// the right funnel message instead of undifferentiated silence. Only accounts with some
    /// registration footprint (own or family-credential, any customer) are acknowledged —
    /// a bare identity row with no registrations reports <c>NotFound</c>.
    /// </summary>
    Task<AdminCandidateMissReason> DiagnoseAdminCandidateMissAsync(
        string query,
        Guid jobId,
        string requestedRoleId,
        IReadOnlyCollection<string> laneRoleIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Find every account a forgot-password submission reaches — legacy AccountController semantics.
    /// A username match wins outright; otherwise the address is matched against
    /// <c>AspNetUsers.NormalizedEmail</c> PLUS family logins whose household record
    /// (<c>Families.MomEmail</c>/<c>DadEmail</c>) holds it. One email legitimately owns many
    /// accounts here, so the caller sends one reset email per account, keyed by UserId.
    /// </summary>
    Task<List<PasswordResetAccount>> GetPasswordResetAccountsAsync(
        string usernameOrEmail,
        CancellationToken cancellationToken = default);
}

public record PasswordResetAccount
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public string? Email { get; init; }
}

public record UserSearchResult
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    /// <summary>"Admin" (lane-pure: all registrations within the granted role's lane) or "PendingAdult" (all Unassigned Adult).</summary>
    public required string AccountType { get; init; }
}

public record UserNameInfo
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

/// <summary>Why an admin-candidate search returned nothing (funnel feedback).</summary>
public enum AdminCandidateMissReason
{
    /// <summary>No matching account with any registration footprint.</summary>
    NotFound,

    /// <summary>A match exists but is a family credential — shared household logins are structurally barred from admin roles.</summary>
    FamilyOrPlayer,

    /// <summary>A match already holds the requested role on this job — manage them in the grid.</summary>
    AlreadyAdmin,

    /// <summary>A match exists but holds no pending (Unassigned Adult) registration and is not lane-pure.</summary>
    OutsideLane,

    /// <summary>A match holds MORE than one active pending registration — ambiguous; remove the extras first.</summary>
    MultiplePending
}

public record UserBasicInfo
{
    public required string UserId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public DateTime? Birthdate { get; init; }
}

public record UserContactInfo
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Cellphone { get; init; }
    public string? Phone { get; init; }
}
