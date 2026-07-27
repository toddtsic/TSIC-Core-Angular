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
    /// Eligible accounts only (AM-004): never a family credential holder, and either every registration
    /// is an admin role (existing admin account) or every registration is Unassigned Adult with at least
    /// one on the given customer (pending adult awaiting elevation). Family/player logins are shared
    /// within a household and are structurally excluded — they must never surface as admin candidates.
    /// </summary>
    Task<List<UserSearchResult>> SearchAdminCandidatesAsync(
        string query,
        Guid customerId,
        int maxResults = 10,
        CancellationToken cancellationToken = default);
}

public record UserSearchResult
{
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    /// <summary>"Admin" (all registrations admin-role) or "PendingAdult" (all Unassigned Adult).</summary>
    public required string AccountType { get; init; }
}

public record UserNameInfo
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
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
