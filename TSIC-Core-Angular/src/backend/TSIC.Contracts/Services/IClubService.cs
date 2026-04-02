using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

public interface IClubService
{
    Task<ClubRepRegistrationResponse> RegisterAsync(ClubRepRegistrationRequest request);
    Task<List<ClubSearchResult>> SearchClubsAsync(string query, string? state);
    Task<AddClubResponse> AddClubAsync(AddClubRequest request, string userId);

    /// <summary>
    /// Invalidate cached club search candidates (call after creating a club).
    /// </summary>
    void InvalidateSearchCache();
}
