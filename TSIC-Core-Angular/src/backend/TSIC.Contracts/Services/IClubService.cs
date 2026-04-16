using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IClubService
{
    Task<ClubRepRegistrationResponse> RegisterAsync(ClubRepRegistrationRequest request);
    Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state);
    Task<AddClubResponse> AddClubAsync(AddClubRequest request, string userId);

    /// <summary>
    /// Read the authenticated user's profile fields (first/last, email, address, phone).
    /// Returns null when the user no longer exists.
    /// </summary>
    Task<ClubRepProfileDto?> GetSelfProfileAsync(string userId);

    /// <summary>
    /// Update the authenticated user's profile fields. Excludes username / password /
    /// club name — those are handled through dedicated flows.
    /// Returns false when the user doesn't exist or Identity rejects the update.
    /// </summary>
    Task<bool> UpdateSelfProfileAsync(string userId, ClubRepProfileUpdateRequest request);

    /// <summary>
    /// Invalidate cached club search candidates (call after creating a club).
    /// </summary>
    void InvalidateSearchCache();
}
