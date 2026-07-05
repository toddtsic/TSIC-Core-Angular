using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Role-neutral self-service profile read/write over the authenticated user's
/// Identity record. Single owner of the ApplicationUser profile-field mutation;
/// the club-rep flow delegates here so the logic lives in one place.
/// </summary>
public interface IUserProfileService
{
    /// <summary>
    /// Read the authenticated user's profile fields (first/last, email, address, phone).
    /// Returns null when the user no longer exists.
    /// </summary>
    Task<UserProfileDto?> GetSelfProfileAsync(string userId);

    /// <summary>
    /// Update the authenticated user's profile fields. Excludes username/password.
    /// Returns false when the user doesn't exist or Identity rejects the update.
    /// </summary>
    Task<bool> UpdateSelfProfileAsync(string userId, UserProfileUpdateRequest request);

    /// <summary>
    /// True when no account already owns <paramref name="username"/>. The name is
    /// normalized the SAME way Identity's <c>UserManager.CreateAsync</c> normalizes it,
    /// so this pre-check and the final create agree. Advisory only — a concurrent
    /// registration can still win the race, which CreateAsync rejects authoritatively.
    /// </summary>
    Task<bool> IsUsernameAvailableAsync(string username);
}
