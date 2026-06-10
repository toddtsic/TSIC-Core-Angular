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
}
