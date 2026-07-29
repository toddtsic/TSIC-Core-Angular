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
    /// Lane model (AM-004): eligibility depends on the role being granted, counting only LIVE
    /// registrations — bActive, and the job unexpired for the role type (admin roles ride
    /// Jobs.ExpiryAdmin, every other role rides Jobs.ExpiryUsers). An account qualifies only if it
    /// is never a family credential holder (global — never liveness-gated) AND either (a) lane-pure —
    /// every live registration lies within <paramref name="laneRoleIds"/> (no cross-type grants) —
    /// or (b) every live registration is Unassigned Adult with at least one on the given customer
    /// (pending adult awaiting elevation).
    /// Family/player logins are shared within a household and are structurally excluded.
    /// Lane-pure accounts already holding ANY registration on <paramref name="jobId"/> (active or
    /// not — mirrors the add-side duplicate guard) are excluded: they're already administrators
    /// here, managed in the grid. Pending adults keep their this-job registration offered — it is
    /// what the convert path consumes.
    /// </summary>
    Task<List<UserSearchResult>> SearchAdminCandidatesAsync(
        string query,
        Guid customerId,
        Guid jobId,
        IReadOnlyCollection<string> laneRoleIds,
        int maxResults = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explains why <see cref="SearchAdminCandidatesAsync"/> came back empty, so the UI can show
    /// the right funnel message instead of undifferentiated silence. Enumeration guard: only
    /// accounts with a registration footprint on the given customer are acknowledged to exist —
    /// a name that matches solely on other customers still reports <c>NotFound</c>.
    /// </summary>
    Task<AdminCandidateMissReason> DiagnoseAdminCandidateMissAsync(
        string query,
        Guid customerId,
        Guid jobId,
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

/// <summary>Why an admin-candidate search returned nothing (AM-004 funnel feedback).</summary>
public enum AdminCandidateMissReason
{
    /// <summary>No matching account with a footprint on this customer.</summary>
    NotFound,

    /// <summary>A match exists but is a family/player credential — structurally barred from admin roles.</summary>
    FamilyOrPlayer,

    /// <summary>A match already holds a lane-role registration on this job — manage them in the grid.</summary>
    AlreadyAdmin,

    /// <summary>A match exists but its registration history lies outside the granted role's lane.</summary>
    OutsideLane
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
